using System.Collections.Concurrent;
using MangaHub.Core.Dto;
using MangaHub.Core.Services;

namespace MangaHub.Api.Services;

public sealed class ReaderPreparationService(IServiceScopeFactory scopeFactory)
{
    private readonly ConcurrentDictionary<Guid, ReaderPreparationJob> jobs = new();

    public ReaderPreparationStatus Start(
        Guid userId,
        Guid entryId,
        Guid? afterCachedChapterId,
        Guid? beforeCachedChapterId)
    {
        RemoveExpiredJobs();

        var jobId = Guid.NewGuid();
        var status = new ReaderPreparationStatus(jobId, "Waiting to prepare the chapter", 0, 0, 0, false, false, "", null);
        jobs[jobId] = new ReaderPreparationJob(userId, DateTimeOffset.UtcNow, status);

        _ = Task.Run(() => PrepareAsync(jobId, userId, entryId, afterCachedChapterId, beforeCachedChapterId));
        return status;
    }

    public ReaderPreparationStatus? Get(Guid jobId, Guid userId) =>
        jobs.TryGetValue(jobId, out var job) && job.UserId == userId ? job.Status : null;

    private async Task PrepareAsync(
        Guid jobId,
        Guid userId,
        Guid entryId,
        Guid? afterCachedChapterId,
        Guid? beforeCachedChapterId)
    {
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
                CancellationToken.None,
                progress);

            if (launch is null)
            {
                Update(jobId, status => status with
                {
                    Stage = "No readable MangaDex chapter was found",
                    IsComplete = true,
                    IsFailed = true,
                    Error = "No readable MangaDex chapter is available."
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

    private sealed class CallbackProgress(Action<ReaderPreparationProgress> report) : IProgress<ReaderPreparationProgress>
    {
        public void Report(ReaderPreparationProgress value) => report(value);
    }
}
