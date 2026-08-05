using System.Collections.Concurrent;
using MangaHub.Core.Dto;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.RemoteJobs;

namespace MangaHub.Api.Services;

public sealed class ReaderPreparationService(
    IServiceScopeFactory scopeFactory,
    RemoteJobPriorityContext priorityContext,
    ILogger<ReaderPreparationService> logger)
{
    private readonly ConcurrentDictionary<Guid, ReaderPreparationJob> jobs = new();
    private readonly ConcurrentDictionary<ReaderPrefetchKey, ReaderPrefetchOperation> activePrefetches = new();

    public ReaderPreparationStatus Start(
        Guid userId,
        Guid entryId,
        Guid? afterCachedChapterId,
        Guid? beforeCachedChapterId,
        string language,
        bool allowLanguageFallback,
        bool allowChapterJump,
        string? requestedChapter = null)
    {
        RemoveExpiredJobs();

        var jobId = Guid.NewGuid();
        var status = new ReaderPreparationStatus(jobId, "Waiting to prepare the chapter", 0, 0, 0, false, false, "", null);
        jobs[jobId] = new ReaderPreparationJob(userId, DateTimeOffset.UtcNow, status);

        if (afterCachedChapterId is not null
            && activePrefetches.TryGetValue(CreatePrefetchKey(userId, entryId, afterCachedChapterId.Value, language), out var prefetch))
        {
            _ = Task.Run(() => ContinueFromPrefetchAsync(
                jobId,
                userId,
                entryId,
                afterCachedChapterId,
                beforeCachedChapterId,
                language,
                allowLanguageFallback,
                allowChapterJump,
                requestedChapter,
                prefetch));
        }
        else
        {
            _ = Task.Run(() => PrepareAsync(
                jobId,
                userId,
                entryId,
                afterCachedChapterId,
                beforeCachedChapterId,
                language,
                allowLanguageFallback,
                allowChapterJump,
                requestedChapter,
                RemoteJobPriority.UserBlocking));
        }
        return status;
    }

    public ReaderPreparationStatus? Get(Guid jobId, Guid userId) =>
        jobs.TryGetValue(jobId, out var job) && job.UserId == userId ? job.Status : null;

    public void PrefetchNext(Guid userId, Guid entryId, Guid afterCachedChapterId, string language)
    {
        var key = CreatePrefetchKey(userId, entryId, afterCachedChapterId, language);
        var prefetch = new ReaderPrefetchOperation();
        if (!activePrefetches.TryAdd(key, prefetch))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            using var priorityScope = priorityContext.Push(RemoteJobPriority.ReaderAhead);
            try
            {
                using var scope = scopeFactory.CreateScope();
                var reader = scope.ServiceProvider.GetRequiredService<ReaderService>();
                var progress = new CallbackProgress(prefetch.Report);
                await reader.PrefetchNextMangaDexChapterAsync(
                    userId,
                    entryId,
                    afterCachedChapterId,
                    language,
                    CancellationToken.None,
                    progress);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Background prefetch failed for MangaDex entry {EntryId} after chapter {ChapterId}.", entryId, afterCachedChapterId);
            }
            finally
            {
                activePrefetches.TryRemove(key, out _);
                prefetch.Complete();
            }
        });
    }

    private async Task ContinueFromPrefetchAsync(
        Guid jobId,
        Guid userId,
        Guid entryId,
        Guid? afterCachedChapterId,
        Guid? beforeCachedChapterId,
        string language,
        bool allowLanguageFallback,
        bool allowChapterJump,
        string? requestedChapter,
        ReaderPrefetchOperation prefetch)
    {
        while (!prefetch.Completion.IsCompleted)
        {
            var progress = prefetch.Progress;
            Update(jobId, status => status with
            {
                Stage = $"Preparing next chapter: {progress.Stage}",
                Progress = progress.Progress,
                CompletedPages = progress.CompletedPages,
                TotalPages = progress.TotalPages
            });
            await Task.WhenAny(prefetch.Completion, Task.Delay(200));
        }

        await prefetch.Completion;
        Update(jobId, status => status with
        {
            Stage = "Finalizing the prefetched chapter",
            Progress = Math.Max(status.Progress, 96)
        });
        await PrepareAsync(
            jobId,
            userId,
            entryId,
            afterCachedChapterId,
            beforeCachedChapterId,
            language,
            allowLanguageFallback,
            allowChapterJump,
            requestedChapter,
            RemoteJobPriority.UserBlocking);
    }

    private async Task PrepareAsync(
        Guid jobId,
        Guid userId,
        Guid entryId,
        Guid? afterCachedChapterId,
        Guid? beforeCachedChapterId,
        string language,
        bool allowLanguageFallback,
        bool allowChapterJump,
        string? requestedChapter,
        RemoteJobPriority priority)
    {
        using var priorityScope = priorityContext.Push(priority);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var reader = scope.ServiceProvider.GetRequiredService<ReaderService>();
            var progress = new CallbackProgress(value =>
                Update(jobId, status => status with
                {
                    Stage = value.Stage,
                    Progress = value.Progress,
                    CompletedPages = value.CompletedPages,
                    TotalPages = value.TotalPages
                }));

            Update(jobId, status => status with { Stage = "Finding a readable MangaDex chapter", Progress = 4 });
            var launch = await reader.PrepareMangaDexChapterAsync(
                userId,
                entryId,
                afterCachedChapterId,
                beforeCachedChapterId,
                language,
                allowLanguageFallback,
                allowChapterJump,
                CancellationToken.None,
                progress,
                requestedChapter: requestedChapter);

            if (launch is null)
            {
                var unavailableMessage = afterCachedChapterId is not null
                    ? "No later readable MangaDex chapter is available."
                    : beforeCachedChapterId is not null
                        ? "No earlier readable MangaDex chapter is available."
                        : "No readable MangaDex chapter is available.";
                Update(jobId, status => status with
                {
                    Stage = "No readable MangaDex chapter was found",
                    IsComplete = true,
                    IsFailed = true,
                    Error = unavailableMessage
                });
                return;
            }

            Update(jobId, status => status with
            {
                Stage = "Opening the local reader",
                Progress = 100,
                IsComplete = true,
                Launch = launch
            });
        }
        catch (ReaderService.MangaCompletedException)
        {
            Update(jobId, status => status with
            {
                Stage = "You have finished the manga",
                Progress = 100,
                IsComplete = true,
                Error = "You have read every chapter in this completed series.",
                IsSeriesComplete = true
            });
        }
        catch (ReaderService.NoNextMangaDexChapterException)
        {
            Update(jobId, status => status with
            {
                Stage = "No later readable MangaDex chapter was found",
                IsComplete = true,
                IsFailed = true,
                Error = "No later readable MangaDex chapter is available."
            });
        }
        catch (ReaderService.MangaDexLanguageFallbackRequiredException ex)
        {
            Update(jobId, status => status with
            {
                Stage = "A newer chapter is available in another language",
                IsComplete = true,
                IsFailed = true,
                Error = "No newer chapter is available in the selected language.",
                AvailableLanguages = ex.Languages
            });
        }
        catch (ReaderService.MangaDexClosestChapterConfirmationRequiredException ex)
        {
            Update(jobId, status => status with
            {
                Stage = "The closest MangaDex chapter needs confirmation",
                IsComplete = true,
                IsFailed = true,
                Error = "The recorded chapter could not be matched exactly.",
                ChapterMatch = ex.ChapterMatch
            });
        }
        catch (ReaderService.MangaDexChapterJumpConfirmationRequiredException ex)
        {
            Update(jobId, status => status with
            {
                Stage = "The next MangaDex chapter has a gap",
                IsComplete = true,
                IsFailed = true,
                Error = "The next available chapter skips part of your reading progress.",
                ChapterJump = ex.ChapterJump
            });
        }
        catch (Exception)
        {
            Update(jobId, status => status with
            {
                Stage = "Chapter preparation failed",
                IsComplete = true,
                IsFailed = true,
                Error = "MangaHub could not prepare this chapter. Check the server log for details."
            });
        }
    }

    private void Update(Guid jobId, Func<ReaderPreparationStatus, ReaderPreparationStatus> update)
    {
        if (jobs.TryGetValue(jobId, out var job))
        {
            jobs[jobId] = job with { Status = update(job.Status) };
        }
    }

    private void RemoveExpiredJobs()
    {
        var expiration = DateTimeOffset.UtcNow.AddHours(-1);
        foreach (var item in jobs.Where(item => item.Value.CreatedAt < expiration))
        {
            jobs.TryRemove(item.Key, out _);
        }
    }

    private sealed record ReaderPreparationJob(Guid UserId, DateTimeOffset CreatedAt, ReaderPreparationStatus Status);
    private sealed record ReaderPrefetchKey(Guid UserId, Guid EntryId, Guid ChapterId, string Language);

    private sealed class ReaderPrefetchOperation
    {
        private ReaderPreparationProgress progress = new("Waiting to prepare the next chapter", 0);
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => completion.Task;
        public ReaderPreparationProgress Progress => Volatile.Read(ref progress);

        public void Report(ReaderPreparationProgress value) => Volatile.Write(ref progress, value);
        public void Complete() => completion.TrySetResult();
    }

    private static ReaderPrefetchKey CreatePrefetchKey(Guid userId, Guid entryId, Guid chapterId, string language) =>
        new(userId, entryId, chapterId, NormalizeLanguage(language));

    private static string NormalizeLanguage(string language) =>
        string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();

    private sealed class CallbackProgress(Action<ReaderPreparationProgress> report) : IProgress<ReaderPreparationProgress>
    {
        public void Report(ReaderPreparationProgress value) => report(value);
    }
}
