using System.Globalization;
using MangaHub.Api.Common;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Sources;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Services;

public sealed class ReaderService(
    ShelfRepository shelf,
    SeriesRepository series,
    IArchiveReader archives,
    UsageTrackingService? usage,
    IMangaDexChapterCache mangaDexCache,
    IOptions<MangaHubOptions> options,
    MangaSourceRegistry sources,
    NotificationService? notifications = null,
    IssueReportingService? issues = null,
    ArchiveRecoveryTelemetryService? archiveTelemetry = null)
{
    private const string MangaDexCacheSource = "mangadex-cache";

    public Task<ReaderLaunchResponse?> PrepareMangaDexChapterAsync(Guid userId, Guid entryId, Guid? afterCachedChapterId, Guid? beforeCachedChapterId, CancellationToken cancellationToken, IProgress<ReaderPreparationProgress>? progress = null, bool updateReadingProgress = true) =>
        PrepareMangaDexChapterAsync(userId, entryId, afterCachedChapterId, beforeCachedChapterId, "en", false, false, cancellationToken, progress, updateReadingProgress);

    public async Task<ReadOptions?> GetReadOptionsAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        var shelfEntry = await shelf.GetReadShelfAsync(userId, entryId, cancellationToken);
        if (shelfEntry?.MangaEntry is null)
        {
            return null;
        }

        var entry = shelfEntry.MangaEntry;
        var mangaDexId = GetMangaDexId(entry);
        (Guid Id, string ChapterNumber, int PageCount)? localFirstChapter = entry.LocalSeriesId is null
            ? null
            : await series.GetFirstChapterAsync(entry.LocalSeriesId.Value, cancellationToken);

        return new ReadOptions(
            entry.Id,
            entry.Title,
            !string.IsNullOrWhiteSpace(mangaDexId),
            entry.FallbackReaderUrl,
            localFirstChapter is not null,
            localFirstChapter is null
                ? ""
                : $"/reader/{localFirstChapter.Value.Id}/{localFirstChapter.Value.PageCount}?entryId={entry.Id}&chapter={Uri.EscapeDataString(localFirstChapter.Value.ChapterNumber)}&source=local{ReaderModeQuery(entry)}");
    }

    public async Task<ReaderLaunchResponse?> PrepareMangaDexChapterAsync(
        Guid userId,
        Guid entryId,
        Guid? afterCachedChapterId,
        Guid? beforeCachedChapterId,
        string language,
        bool allowLanguageFallback,
        bool allowChapterJump,
        CancellationToken cancellationToken,
        IProgress<ReaderPreparationProgress>? progress = null,
        bool updateReadingProgress = true,
        string? requestedChapter = null,
        string imageQuality = "original")
    {
        var shelfEntry = await shelf.GetWithMangaAsync(userId, entryId, cancellationToken);
        if (shelfEntry?.MangaEntry is null)
        {
            return null;
        }

        var entry = shelfEntry.MangaEntry;
        var mangaDexId = GetMangaDexId(entry);
        if (string.IsNullOrWhiteSpace(mangaDexId))
        {
            return null;
        }
        if (afterCachedChapterId is not null && beforeCachedChapterId is not null)
        {
            return null;
        }
        var preferredLanguages = LanguagePreferences.Parse(language);
        var quality = NormalizeImageQuality(imageQuality);
        var preferredLanguage = preferredLanguages[0];

        var cachedSeries = await series.GetBySourceAndExternalIdAsync(MangaDexCacheSource, mangaDexId, cancellationToken);
        var isNewCachedSeries = cachedSeries is null;
        MangaChapter? cachedChapter = null;
        MangaSourceChapter? sourceChapter = null;

        if (afterCachedChapterId is not null || beforeCachedChapterId is not null)
        {
            var currentChapterId = afterCachedChapterId ?? beforeCachedChapterId!.Value;
            var current = await series.GetChapterWithSeriesAsync(currentChapterId, cancellationToken);
            if (current?.Series is null
                || !string.Equals(current.Series.Source, MangaDexCacheSource, StringComparison.Ordinal)
                || !string.Equals(current.Series.ExternalId, mangaDexId, StringComparison.Ordinal))
            {
                return null;
            }

            cachedChapter = FindAdjacentCachedChapter(cachedSeries, current.ChapterNumber, preferredLanguages, afterCachedChapterId is not null, quality);
            if (cachedChapter is not null
                && afterCachedChapterId is not null
                && IsChapterJump(current.ChapterNumber, cachedChapter.ChapterNumber))
            {
                var remoteCandidate = await FindNextMangaDexChapterAfterNumberAsync(mangaDexId, current.ChapterNumber, preferredLanguages, cancellationToken);
                if (remoteCandidate is not null && ShouldPreferRemoteCandidate(remoteCandidate, cachedChapter, preferredLanguages))
                {
                    sourceChapter = remoteCandidate;
                    cachedChapter = cachedSeries?.Chapters.FirstOrDefault(chapter => chapter.SourceId == sourceChapter.Id && chapter.ImageQuality == quality);
                }
            }
            if (cachedChapter is null)
            {
                sourceChapter ??= afterCachedChapterId is not null
                    ? await FindNextMangaDexChapterAfterNumberAsync(mangaDexId, current.ChapterNumber, preferredLanguages, cancellationToken)
                    : await FindPreviousMangaDexChapterBeforeNumberAsync(mangaDexId, current.ChapterNumber, preferredLanguages, cancellationToken);
                if (sourceChapter is null)
                {
                    var availableLanguages = afterCachedChapterId is not null
                        ? await FindAvailableLanguagesAfterNumberAsync(mangaDexId, current.ChapterNumber, preferredLanguages, cancellationToken)
                        : await FindAvailableLanguagesBeforeNumberAsync(mangaDexId, current.ChapterNumber, preferredLanguages, cancellationToken);
                    if (!allowLanguageFallback && availableLanguages.Count > 0)
                    {
                        throw new MangaDexLanguageFallbackRequiredException(availableLanguages);
                    }
                    if (afterCachedChapterId is not null
                        && CanMarkShelfEntryDone(shelfEntry, current.ChapterNumber)
                        && await IsPublishingCompleteAsync(entry, cancellationToken))
                    {
                        await MarkShelfEntryDoneAsync(shelfEntry, cancellationToken);
                        throw new MangaCompletedException();
                    }
                    return null;
                }

                cachedChapter = cachedSeries?.Chapters.FirstOrDefault(chapter => chapter.SourceId == sourceChapter.Id && chapter.ImageQuality == quality);
            }
            if (afterCachedChapterId is not null
                && !allowChapterJump
                && IsChapterJump(current.ChapterNumber, sourceChapter?.Number ?? cachedChapter?.ChapterNumber ?? ""))
            {
                var nextChapter = sourceChapter?.Number ?? cachedChapter!.ChapterNumber;
                var nextLanguage = sourceChapter?.Language ?? cachedChapter!.Language;
                throw new MangaDexChapterJumpConfirmationRequiredException(
                    current.ChapterNumber,
                    nextChapter,
                    NormalizeLanguage(nextLanguage),
                    await FindCloserNextChapterLanguagesAsync(mangaDexId, current.ChapterNumber, nextChapter, preferredLanguages, cancellationToken));
            }
        }
        else if (shelfEntry.IsRead && !string.IsNullOrWhiteSpace(shelfEntry.CurrentChapter))
        {
            sourceChapter = await FindNextMangaDexChapterAfterNumberAsync(mangaDexId, shelfEntry.CurrentChapter, preferredLanguages, cancellationToken);
            if (sourceChapter is null)
            {
                cachedChapter = FindAdjacentCachedChapter(cachedSeries, shelfEntry.CurrentChapter, preferredLanguages, next: true, quality);
                if (cachedChapter is null)
                {
                    var availableLanguages = await FindAvailableLanguagesAfterNumberAsync(mangaDexId, shelfEntry.CurrentChapter, preferredLanguages, cancellationToken);
                    if (!allowLanguageFallback && availableLanguages.Count > 0)
                    {
                        throw new MangaDexLanguageFallbackRequiredException(availableLanguages);
                    }

                    await RecordCompletedMangaDexChapterAsync(entry, shelfEntry.CurrentChapter, preferredLanguages, cancellationToken);
                    if (await IsPublishingCompleteAsync(entry, cancellationToken))
                    {
                        await MarkShelfEntryDoneAsync(shelfEntry, cancellationToken);
                        throw new MangaCompletedException();
                    }
                    throw new NoNextMangaDexChapterException();
                }
            }

            if (sourceChapter is not null
                && !allowChapterJump
                && IsChapterJump(shelfEntry.CurrentChapter, sourceChapter.Number))
            {
                throw new MangaDexChapterJumpConfirmationRequiredException(
                    shelfEntry.CurrentChapter,
                    sourceChapter.Number,
                    NormalizeLanguage(sourceChapter.Language),
                    await FindCloserNextChapterLanguagesAsync(mangaDexId, shelfEntry.CurrentChapter, sourceChapter.Number, preferredLanguages, cancellationToken));
            }

            if (sourceChapter is not null)
            {
                cachedChapter = cachedSeries?.Chapters.FirstOrDefault(chapter => chapter.SourceId == sourceChapter.Id && chapter.ImageQuality == quality);
            }
        }
        else if (cachedSeries is not null && !string.IsNullOrWhiteSpace(shelfEntry.CurrentChapter))
        {
            cachedChapter = cachedSeries.Chapters
                .Where(chapter => LanguagePreferences.Contains(preferredLanguages, chapter.Language))
                .OrderBy(chapter => LanguagePreferences.IndexOf(preferredLanguages, chapter.Language))
                .ThenBy(chapter => chapter.CreatedAt)
                .FirstOrDefault(chapter => chapter.ImageQuality == quality && HasExactChapter(chapter.ChapterNumber, shelfEntry.CurrentChapter));
        }

        var recoveringArchivedChapter = false;
        if (cachedChapter is not null && !HasReadableCachedArchive(cachedChapter, mangaDexId))
        {
            progress?.Report(new ReaderPreparationProgress("Restoring the archived local chapter", 12));
            if (await mangaDexCache.RestoreArchivedAsync(mangaDexId, cachedChapter.SourceId, cancellationToken, quality))
            {
                await RecordArchiveRecoveryAsync(mangaDexId, cachedChapter.SourceId, "restored", cancellationToken);
            }
            else
            {
                recoveringArchivedChapter = true;
                progress?.Report(new ReaderPreparationProgress("Refreshing an unreadable local chapter", 12));
                await mangaDexCache.DeleteAsync(mangaDexId, cachedChapter.SourceId, cancellationToken, quality);
                cachedChapter = null;
            }
        }

        var isInitialTrackedChapterSelection = sourceChapter is null
            && afterCachedChapterId is null
            && beforeCachedChapterId is null
            && !shelfEntry.IsRead;
        if (cachedChapter is null)
        {
            var mangaDex = sources.Get("mangadex");
            progress?.Report(new ReaderPreparationProgress("Loading MangaDex chapter list", 8));
            var preferredChapters = await GetPreferredMangaDexChaptersAsync(mangaDexId, shelfEntry.CurrentChapter, preferredLanguages, cancellationToken);
            if (preferredChapters.Count == 0
                && (await mangaDex.GetChaptersAsync(mangaDexId, null, cancellationToken)).Count == 0)
            {
                cachedChapter = FindCachedChapterForRemoteMiss(cachedSeries, shelfEntry, requestedChapter, preferredLanguages, quality);
                if (cachedChapter is null)
                {
                    throw new MangaDexUnavailableException();
                }
            }
            if (isInitialTrackedChapterSelection
                && !allowLanguageFallback
                && !string.IsNullOrWhiteSpace(shelfEntry.CurrentChapter)
                && !HasExactChapter(preferredChapters, shelfEntry.CurrentChapter))
            {
                var availableLanguages = await FindAvailableLanguagesForChapterAsync(
                    mangaDexId,
                    shelfEntry.CurrentChapter,
                    preferredLanguages,
                    cancellationToken);
                if (availableLanguages.Count > 0)
                {
                    throw new MangaDexLanguageFallbackRequiredException(availableLanguages);
                }
            }

            sourceChapter ??= string.IsNullOrWhiteSpace(requestedChapter)
                ? SelectCurrentMangaDexChapter(preferredChapters, shelfEntry.CurrentChapter)
                : preferredChapters.FirstOrDefault(chapter => HasExactChapter(chapter.Number, requestedChapter));
            if (sourceChapter is null)
            {
                cachedChapter ??= FindCachedChapterForRemoteMiss(cachedSeries, shelfEntry, requestedChapter, preferredLanguages, quality);
                if (cachedChapter is null)
                {
                    return null;
                }

                goto CachedChapterResolved;
            }

            cachedChapter ??= sourceChapter is null
                ? null
                : cachedSeries?.Chapters.FirstOrDefault(chapter => chapter.SourceId == sourceChapter.Id && chapter.ImageQuality == quality);
            if (isInitialTrackedChapterSelection
                && sourceChapter is not null
                && string.Equals(shelfEntry.ReadingStatus, "planned", StringComparison.OrdinalIgnoreCase)
                && !allowChapterJump
                && !HasExactChapter(sourceChapter.Number, "1")
                && !HasZeroBasedSeriesStart(preferredChapters))
            {
                throw new MangaDexChapterJumpConfirmationRequiredException(
                    "1",
                    sourceChapter.Number,
                    NormalizeLanguage(sourceChapter.Language),
                    await FindCloserNextChapterLanguagesAsync(mangaDexId, "1", sourceChapter.Number, preferredLanguages, cancellationToken));
            }
            if (isInitialTrackedChapterSelection
                && sourceChapter is not null
                && !allowLanguageFallback
                && !string.IsNullOrWhiteSpace(shelfEntry.CurrentChapter)
                && !HasExactChapter(sourceChapter.Number, shelfEntry.CurrentChapter))
            {
                throw new MangaDexClosestChapterConfirmationRequiredException(
                    shelfEntry.CurrentChapter,
                    sourceChapter.Number,
                    NormalizeLanguage(sourceChapter.Language));
            }

            var selectedSourceChapter = sourceChapter!;
            MangaDexCachedChapter? cachedArchive = null;
            var originalCachedChapter = string.Equals(quality, "data-saver", StringComparison.OrdinalIgnoreCase)
                ? cachedSeries?.Chapters.FirstOrDefault(chapter => chapter.SourceId == selectedSourceChapter.Id && chapter.ImageQuality == "original")
                : null;
            if (originalCachedChapter is not null)
            {
                try
                {
                    progress?.Report(new ReaderPreparationProgress("Creating a Data Saver copy from the local chapter", 20));
                    cachedArchive = await mangaDexCache.CreateDataSaverFromOriginalAsync(mangaDexId, selectedSourceChapter.Id, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Fall through to MangaDex Data Saver when a local original cannot be converted.
                }
            }

            if (cachedArchive is null)
            {
                progress?.Report(new ReaderPreparationProgress("Loading the MangaDex page list", 20));
                var pages = await mangaDex.GetPagesAsync(selectedSourceChapter.Id, cancellationToken, quality);
                cachedArchive = await mangaDexCache.EnsureCachedAsync(mangaDexId, selectedSourceChapter.Id, pages, cancellationToken, progress, quality);
            }
            if (recoveringArchivedChapter)
            {
                await RecordArchiveRecoveryAsync(mangaDexId, selectedSourceChapter.Id, "redownloaded", cancellationToken);
            }
            cachedSeries ??= CreateCachedSeries(entry, mangaDexId);
            if (isNewCachedSeries)
            {
                series.AddSeries(cachedSeries);
            }

            cachedChapter = cachedSeries.Chapters.FirstOrDefault(chapter => chapter.SourceId == selectedSourceChapter.Id && chapter.ImageQuality == quality);
            if (cachedChapter is null)
            {
                cachedChapter = new MangaChapter
                {
                    Series = cachedSeries,
                    ChapterNumber = selectedSourceChapter.Number,
                    Language = selectedSourceChapter.Language,
                    ImageQuality = quality,
                    Title = selectedSourceChapter.Title,
                    SourceId = selectedSourceChapter.Id,
                    PageCount = cachedArchive.PageCount,
                    FileHash = cachedArchive.FileHash
                };
                cachedSeries.Chapters.Add(cachedChapter);
                series.AddChapter(cachedChapter);
            }
            else
            {
                cachedChapter.ChapterNumber = selectedSourceChapter.Number;
                cachedChapter.Language = selectedSourceChapter.Language;
                cachedChapter.ImageQuality = quality;
                cachedChapter.Title = selectedSourceChapter.Title;
                cachedChapter.PageCount = cachedArchive.PageCount;
                cachedChapter.FileHash = cachedArchive.FileHash;
            }
        }

    CachedChapterResolved:
        if (cachedChapter is null)
        {
            return null;
        }

        // Background prefetch prepares a chapter for a possible future read; it is
        // not reader activity and must not extend the cache retention grace period.
        if (updateReadingProgress)
        {
            cachedChapter.LastReaderOpenedAt = DateTimeOffset.UtcNow;
        }

        var resolvedLanguage = NormalizeLanguage(cachedChapter.Language);
        if (isInitialTrackedChapterSelection
            && string.Equals(shelfEntry.ReadingStatus, "planned", StringComparison.OrdinalIgnoreCase)
            && !allowLanguageFallback
            && !LanguagePreferences.Contains(preferredLanguages, resolvedLanguage))
        {
            var availableLanguages = await FindAvailableLanguagesForChapterAsync(mangaDexId, cachedChapter.ChapterNumber, preferredLanguages, cancellationToken);
            if (!availableLanguages.Contains(resolvedLanguage, StringComparer.OrdinalIgnoreCase))
            {
                availableLanguages.Add(resolvedLanguage);
            }
            throw new MangaDexLanguageFallbackRequiredException(availableLanguages);
        }

        var shouldAdvanceReadingProgress = updateReadingProgress && beforeCachedChapterId is null;
        if (shouldAdvanceReadingProgress)
        {
            var startedReading = string.Equals(shelfEntry.ReadingStatus, "planned", StringComparison.OrdinalIgnoreCase);
            shelfEntry.CurrentChapter = cachedChapter.ChapterNumber;
            shelfEntry.IsRead = false;
            if (string.Equals(shelfEntry.ReadingStatus, "planned", StringComparison.OrdinalIgnoreCase))
            {
                shelfEntry.ReadingStatus = "reading";
            }
            shelfEntry.UpdatedAt = DateTimeOffset.UtcNow;

            progress?.Report(new ReaderPreparationProgress("Saving your reading progress", 98));
            await shelf.SaveChangesAsync(cancellationToken);
            if (startedReading && usage is not null) await usage.TrackAsync(userId, UsageEventTypes.MangaStarted, entry.Id, cancellationToken);
        }
        else
        {
            await series.SaveChangesAsync(cancellationToken);
        }

        progress?.Report(new ReaderPreparationProgress(
            shouldAdvanceReadingProgress ? "Opening the local reader" : "The chapter is ready", 100));
        return new ReaderLaunchResponse(
            $"/reader/{cachedChapter.Id}/{cachedChapter.PageCount}?entryId={entry.Id}&chapter={Uri.EscapeDataString(cachedChapter.ChapterNumber)}&source=mangadex&language={Uri.EscapeDataString(resolvedLanguage)}&quality={quality}{ReaderModeQuery(entry)}",
            cachedChapter.ChapterNumber,
            cachedChapter.PageCount);
    }

    public async Task<MangaDexLanguagesResponse?> GetMangaDexLanguagesAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        var shelfEntry = await shelf.GetWithMangaAsync(userId, entryId, cancellationToken);
        var mangaDexId = shelfEntry?.MangaEntry is null ? "" : GetMangaDexId(shelfEntry.MangaEntry);
        if (string.IsNullOrWhiteSpace(mangaDexId))
        {
            return null;
        }

        var languages = (await sources.Get("mangadex").GetChaptersAsync(mangaDexId, null, cancellationToken))
            .Select(chapter => NormalizeLanguage(chapter.Language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(language => string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(language => language, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new MangaDexLanguagesResponse(mangaDexId, languages);
    }

    public async Task PrefetchNextMangaDexChapterAsync(
        Guid userId,
        Guid entryId,
        Guid afterCachedChapterId,
        string language,
        string imageQuality,
        CancellationToken cancellationToken,
        IProgress<ReaderPreparationProgress>? progress = null) =>
        await PrepareMangaDexChapterAsync(
            userId,
            entryId,
            afterCachedChapterId,
            null,
            language,
            allowLanguageFallback: false,
            allowChapterJump: false,
            cancellationToken,
            progress,
            updateReadingProgress: false,
            requestedChapter: null,
            imageQuality: imageQuality);

    public Task PrefetchNextMangaDexChapterAsync(Guid userId, Guid entryId, Guid afterCachedChapterId, CancellationToken cancellationToken) =>
        PrefetchNextMangaDexChapterAsync(userId, entryId, afterCachedChapterId, "en", "original", cancellationToken);

    public async Task<bool> MarkCurrentChapterReadAsync(
        Guid userId,
        Guid entryId,
        Guid chapterId,
        CancellationToken cancellationToken)
    {
        var shelfEntry = await shelf.GetWithMangaAsync(userId, entryId, cancellationToken);
        var chapter = await series.GetChapterWithSeriesAsync(chapterId, cancellationToken);
        if (shelfEntry?.MangaEntry is null
            || chapter?.Series is null
            || !HasExactChapter(chapter.ChapterNumber, shelfEntry.CurrentChapter)
            || !IsChapterForEntry(chapter, shelfEntry.MangaEntry))
        {
            return false;
        }

        shelfEntry.IsRead = true;
        shelfEntry.UpdatedAt = DateTimeOffset.UtcNow;
        await shelf.SaveChangesAsync(cancellationToken);
        if (notifications is not null)
        {
            await notifications.MarkReleaseNotificationsReadThroughAsync(userId, entryId, shelfEntry.CurrentChapter, cancellationToken);
        }
        if (usage is not null) await usage.TrackAsync(userId, UsageEventTypes.ChapterCompleted, entryId, chapterId, "", $"chapter-complete:{entryId}:{chapterId}", null, cancellationToken);
        return true;
    }

    public async Task<ArchivePage?> GetPageAsync(Guid chapterId, int pageIndex, CancellationToken cancellationToken)
    {
        var chapter = await series.GetChapterWithSeriesAsync(chapterId, cancellationToken);
        if (chapter?.Series is null || pageIndex < 0)
        {
            return null;
        }

        var root = GetReaderRoot(chapter.Series.Source);
        if (root is null)
        {
            return null;
        }

        var relativePath = string.Equals(chapter.Series.Source, MangaDexCacheSource, StringComparison.Ordinal)
            ? Path.Combine("mangadex", chapter.Series.ExternalId, $"{chapter.SourceId}.cbz")
            : chapter.SourceId;
        var archivePath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!archivePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await archives.ReadPageAsync(archivePath, pageIndex, cancellationToken);
    }

    private async Task<MangaSourceChapter?> FindNextMangaDexChapterAfterNumberAsync(string mangaDexId, string currentChapterNumber, string language, CancellationToken cancellationToken)
    {
        var chapters = await sources.Get("mangadex").GetChaptersAsync(mangaDexId, language, cancellationToken);
        return chapters
            .Where(chapter => IsLaterChapter(chapter.Number, currentChapterNumber))
            .OrderBy(chapter => chapter.Number, ChapterNumberComparer.Instance)
            .FirstOrDefault();
    }

    public async Task<string> MarkMangaDexUnavailableAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        var shelfEntry = await shelf.GetWithMangaAsync(userId, entryId, cancellationToken);
        var manga = shelfEntry?.MangaEntry;
        if (manga is null || string.IsNullOrWhiteSpace(manga.MangaDexId)) return manga?.FallbackReaderUrl ?? "";

        var previousMangaDexId = manga.MangaDexId;
        manga.MangaDexId = "";
        manga.MangaDexLatestChapter = null;
        manga.MangaDexLastSyncedAt = DateTimeOffset.UtcNow;
        manga.UpdatedAt = DateTimeOffset.UtcNow;
        if (issues is not null)
        {
            await issues.OpenMangaDexUnavailableIssueAsync(manga, previousMangaDexId, cancellationToken);
        }
        else
        {
            await shelf.SaveChangesAsync(cancellationToken);
        }

        return manga.FallbackReaderUrl;
    }

    public async Task<string> ReportMangaDexLanguageCoverageAsync(Guid userId, Guid entryId, string preferredLanguages, IReadOnlyList<string> availableLanguages, CancellationToken cancellationToken)
    {
        var shelfEntry = await shelf.GetWithMangaAsync(userId, entryId, cancellationToken);
        var manga = shelfEntry?.MangaEntry;
        if (manga is null || string.IsNullOrWhiteSpace(manga.MangaDexId)) return manga?.FallbackReaderUrl ?? "";
        if (issues is not null)
        {
            await issues.OpenMangaDexLanguageCoverageIssueAsync(userId, manga, LanguagePreferences.Parse(preferredLanguages), availableLanguages, cancellationToken);
        }
        return manga.FallbackReaderUrl;
    }

    private async Task<MangaSourceChapter?> FindNextMangaDexChapterAfterNumberAsync(string mangaDexId, string currentChapterNumber, IReadOnlyList<string> preferredLanguages, CancellationToken cancellationToken)
    {
        var candidates = new List<(MangaSourceChapter Chapter, int LanguagePriority)>();
        for (var index = 0; index < preferredLanguages.Count; index++)
        {
            var language = preferredLanguages[index];
            var chapter = await FindNextMangaDexChapterAfterNumberAsync(mangaDexId, currentChapterNumber, language, cancellationToken);
            if (chapter is not null)
            {
                candidates.Add((chapter, index));
            }
        }

        return candidates
            .OrderBy(candidate => candidate.Chapter.Number, ChapterNumberComparer.Instance)
            .ThenBy(candidate => candidate.LanguagePriority)
            .Select(candidate => candidate.Chapter)
            .FirstOrDefault();
    }

    private static bool ShouldPreferRemoteCandidate(MangaSourceChapter remoteCandidate, MangaChapter cachedCandidate, IReadOnlyList<string> preferredLanguages)
    {
        var chapterComparison = ChapterNumberComparer.Instance.Compare(remoteCandidate.Number, cachedCandidate.ChapterNumber);
        return chapterComparison < 0
            || (chapterComparison == 0
                && LanguagePreferences.IndexOf(preferredLanguages, remoteCandidate.Language) < LanguagePreferences.IndexOf(preferredLanguages, cachedCandidate.Language));
    }

    private async Task<List<string>> FindAvailableLanguagesAfterNumberAsync(string mangaDexId, string currentChapterNumber, IReadOnlyList<string> preferredLanguages, CancellationToken cancellationToken)
    {
        return (await sources.Get("mangadex").GetChaptersAsync(mangaDexId, null, cancellationToken))
            .Where(chapter => !LanguagePreferences.Contains(preferredLanguages, chapter.Language))
            .Where(chapter => IsLaterChapter(chapter.Number, currentChapterNumber))
            .Select(chapter => NormalizeLanguage(chapter.Language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<MangaSourceChapter?> FindPreviousMangaDexChapterBeforeNumberAsync(string mangaDexId, string currentChapterNumber, string language, CancellationToken cancellationToken)
    {
        var chapters = await sources.Get("mangadex").GetChaptersAsync(mangaDexId, language, cancellationToken);
        return chapters
            .Where(chapter => IsEarlierChapter(chapter.Number, currentChapterNumber))
            .OrderByDescending(chapter => chapter.Number, ChapterNumberComparer.Instance)
            .FirstOrDefault();
    }

    private async Task<MangaSourceChapter?> FindPreviousMangaDexChapterBeforeNumberAsync(string mangaDexId, string currentChapterNumber, IReadOnlyList<string> preferredLanguages, CancellationToken cancellationToken)
    {
        foreach (var language in preferredLanguages)
        {
            var chapter = await FindPreviousMangaDexChapterBeforeNumberAsync(mangaDexId, currentChapterNumber, language, cancellationToken);
            if (chapter is not null) return chapter;
        }

        return null;
    }

    private async Task<List<string>> FindAvailableLanguagesBeforeNumberAsync(string mangaDexId, string currentChapterNumber, IReadOnlyList<string> preferredLanguages, CancellationToken cancellationToken)
    {
        return (await sources.Get("mangadex").GetChaptersAsync(mangaDexId, null, cancellationToken))
            .Where(chapter => !LanguagePreferences.Contains(preferredLanguages, chapter.Language))
            .Where(chapter => IsEarlierChapter(chapter.Number, currentChapterNumber))
            .Select(chapter => NormalizeLanguage(chapter.Language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<string>> FindAvailableLanguagesForChapterAsync(string mangaDexId, string currentChapterNumber, IReadOnlyList<string> preferredLanguages, CancellationToken cancellationToken) =>
        (await sources.Get("mangadex").GetChaptersAsync(mangaDexId, null, cancellationToken))
            .Where(chapter => !LanguagePreferences.Contains(preferredLanguages, chapter.Language))
            .Where(chapter => HasExactChapter([chapter], currentChapterNumber))
            .Select(chapter => NormalizeLanguage(chapter.Language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task<List<string>> FindCloserNextChapterLanguagesAsync(
        string mangaDexId,
        string currentChapterNumber,
        string proposedChapterNumber,
        IReadOnlyList<string> preferredLanguages,
        CancellationToken cancellationToken)
    {
        return (await sources.Get("mangadex").GetChaptersAsync(mangaDexId, null, cancellationToken))
            .Where(chapter => IsLaterChapter(chapter.Number, currentChapterNumber)
                && IsEarlierChapter(chapter.Number, proposedChapterNumber))
            .Select(chapter => NormalizeLanguage(chapter.Language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(language => LanguagePreferences.IndexOf(preferredLanguages, language))
            .ThenBy(language => language, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<MangaSourceChapter>> GetPreferredMangaDexChaptersAsync(
        string mangaDexId,
        string currentChapterNumber,
        IReadOnlyList<string> preferredLanguages,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MangaSourceChapter>? firstAvailable = null;
        foreach (var language in preferredLanguages)
        {
            var chapters = (await sources.Get("mangadex").GetChaptersAsync(mangaDexId, language, cancellationToken))
                .Where(chapter => chapter.PageCount > 0)
                .ToList();
            if (chapters.Count == 0)
            {
                continue;
            }

            firstAvailable ??= chapters;
            if (string.IsNullOrWhiteSpace(currentChapterNumber) || HasExactChapter(chapters, currentChapterNumber)) return chapters;
        }

        return firstAvailable ?? [];
    }

    private async Task RecordCompletedMangaDexChapterAsync(
        MangaEntry entry,
        string currentChapterNumber,
        IReadOnlyList<string> preferredLanguages,
        CancellationToken cancellationToken)
    {
        var chapterNumber = ParseChapterNumber(currentChapterNumber);
        if (chapterNumber is null)
        {
            return;
        }

        entry.MangaDexLatestChapter = chapterNumber.Value;
        entry.ChapterCount = (int)Math.Floor(chapterNumber.Value);
        entry.MangaDexLastSyncedAt = DateTimeOffset.UtcNow;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        await shelf.SaveChangesAsync(cancellationToken);
        await shelf.ClampMangaDexLanguageLatestChaptersAsync(entry.Id, preferredLanguages, chapterNumber.Value, cancellationToken);
    }

    private async Task MarkShelfEntryDoneAsync(UserMangaEntry shelfEntry, CancellationToken cancellationToken)
    {
        shelfEntry.ReadingStatus = "done";
        shelfEntry.IsRead = true;
        shelfEntry.UpdatedAt = DateTimeOffset.UtcNow;
        await shelf.SaveChangesAsync(cancellationToken);
        if (usage is not null) await usage.TrackAsync(shelfEntry.UserId, UsageEventTypes.MangaCompleted, shelfEntry.MangaEntryId, cancellationToken);
    }

    private async Task<bool> IsPublishingCompleteAsync(MangaEntry entry, CancellationToken cancellationToken)
    {
        var mangaDexId = GetMangaDexId(entry);
        if (string.IsNullOrWhiteSpace(mangaDexId))
        {
            return false;
        }

        try
        {
            // MangaDex is the active reader source, so its live status wins over stale catalog metadata.
            var mangaDexSeries = await sources.Get("mangadex").GetSeriesAsync(mangaDexId, cancellationToken);
            if (mangaDexSeries is null)
            {
                return false;
            }

            if (IsPublishingOngoing(mangaDexSeries.Status))
            {
                return false;
            }

            if (IsCompletionStatus(mangaDexSeries.Status))
            {
                return true;
            }
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        // MangaUpdates is only used when MangaDex returned a known non-ongoing status.
        return entry.MangaUpdatesCompleted == true;
    }

    private static bool CanMarkShelfEntryDone(UserMangaEntry shelfEntry, string chapterNumber) =>
        shelfEntry.IsRead
        && HasExactChapter(shelfEntry.CurrentChapter, chapterNumber)
        && string.Equals(shelfEntry.ReadingStatus, "reading", StringComparison.OrdinalIgnoreCase);

    private static bool IsPublishingOngoing(string? status) =>
        status?.Trim().ToLowerInvariant() is "ongoing" or "hiatus";

    private static bool IsCompletionStatus(string? status) =>
        status?.Trim().ToLowerInvariant() is "finished" or "complete" or "completed" or "done" or "ended";

    private static string ReaderModeQuery(MangaEntry entry) => UsesVerticalReader(entry) ? "&vertical=true" : "";

    private static bool UsesVerticalReader(MangaEntry entry) =>
        entry.MediaType.Contains("manhwa", StringComparison.OrdinalIgnoreCase)
        || entry.MediaType.Contains("webtoon", StringComparison.OrdinalIgnoreCase);

    private static MangaSourceChapter? SelectCurrentMangaDexChapter(IReadOnlyList<MangaSourceChapter> chapters, string currentChapter)
    {
        if (string.IsNullOrWhiteSpace(currentChapter))
        {
            return chapters
                .OrderBy(chapter => chapter.Number, ChapterNumberComparer.Instance)
                .FirstOrDefault();
        }

        var exact = chapters.FirstOrDefault(chapter => string.Equals(chapter.Number, currentChapter, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        if (!TryGetChapterParts(currentChapter, out _))
        {
            return null;
        }

        var numericMatch = chapters.FirstOrDefault(chapter => ChapterNumberComparer.HasSameNumericParts(chapter.Number, currentChapter));
        if (numericMatch is not null)
        {
            return numericMatch;
        }

        return chapters
            .Where(chapter => IsSameOrLaterChapter(chapter.Number, currentChapter))
            .OrderBy(chapter => chapter.Number, ChapterNumberComparer.Instance)
            .FirstOrDefault()
            ?? chapters.OrderByDescending(chapter => chapter.Number, ChapterNumberComparer.Instance).FirstOrDefault();
    }

    private static bool HasExactChapter(IReadOnlyList<MangaSourceChapter> chapters, string currentChapter) =>
        chapters.Any(chapter => HasExactChapter(chapter.Number, currentChapter));

    private static bool HasExactChapter(string chapterNumber, string currentChapter) =>
        string.Equals(chapterNumber, currentChapter, StringComparison.OrdinalIgnoreCase)
        || ChapterNumberComparer.HasSameNumericParts(chapterNumber, currentChapter);

    private static bool HasZeroBasedSeriesStart(IReadOnlyList<MangaSourceChapter> chapters) =>
        chapters.Any(chapter => HasExactChapter(chapter.Number, "0"));

    private static bool IsChapterJump(string currentChapter, string nextChapter)
    {
        if (!TryGetChapterParts(currentChapter, out var currentParts)
            || !TryGetChapterParts(nextChapter, out var nextParts)
            || nextParts[0] <= currentParts[0])
        {
            return false;
        }

        var baseGap = nextParts[0] - currentParts[0];
        if (baseGap > 1)
        {
            return true;
        }

        // A normal chapter can be followed by the first subchapter of the next one
        // (for example 1 -> 2.1). Later subdivisions imply a missing 2.1.
        return currentParts.Length == 1
            && nextParts.Length > 1
            && nextParts[1] > 1;
    }

    private static MangaChapter? FindAdjacentCachedChapter(MangaSeries? cachedSeries, string currentChapter, IReadOnlyList<string> preferredLanguages, bool next, string imageQuality)
    {
        if (cachedSeries is null)
        {
            return null;
        }

        var candidates = cachedSeries.Chapters
            .Where(chapter => string.Equals(chapter.ImageQuality, imageQuality, StringComparison.OrdinalIgnoreCase))
            .Where(chapter => LanguagePreferences.Contains(preferredLanguages, chapter.Language))
            .Where(chapter => next
                ? IsLaterChapter(chapter.ChapterNumber, currentChapter)
                : IsEarlierChapter(chapter.ChapterNumber, currentChapter));

        return next
            ? candidates.OrderBy(chapter => chapter.ChapterNumber, ChapterNumberComparer.Instance).ThenBy(chapter => LanguagePreferences.IndexOf(preferredLanguages, chapter.Language)).FirstOrDefault()
            : candidates.OrderByDescending(chapter => chapter.ChapterNumber, ChapterNumberComparer.Instance).ThenBy(chapter => LanguagePreferences.IndexOf(preferredLanguages, chapter.Language)).FirstOrDefault();
    }

    private static MangaChapter? FindCachedChapterForRemoteMiss(
        MangaSeries? cachedSeries,
        UserMangaEntry shelfEntry,
        string? requestedChapter,
        IReadOnlyList<string> preferredLanguages,
        string imageQuality)
    {
        if (cachedSeries is null)
        {
            return null;
        }

        if (shelfEntry.IsRead && !string.IsNullOrWhiteSpace(shelfEntry.CurrentChapter))
        {
            return FindAdjacentCachedChapter(cachedSeries, shelfEntry.CurrentChapter, preferredLanguages, next: true, imageQuality);
        }

        var targetChapter = string.IsNullOrWhiteSpace(requestedChapter) ? shelfEntry.CurrentChapter : requestedChapter;
        var candidates = cachedSeries.Chapters
            .Where(chapter => string.Equals(chapter.ImageQuality, imageQuality, StringComparison.OrdinalIgnoreCase))
            .Where(chapter => LanguagePreferences.Contains(preferredLanguages, chapter.Language));

        if (!string.IsNullOrWhiteSpace(targetChapter))
        {
            return candidates
                .Where(chapter => HasExactChapter(chapter.ChapterNumber, targetChapter))
                .OrderBy(chapter => LanguagePreferences.IndexOf(preferredLanguages, chapter.Language))
                .ThenBy(chapter => chapter.CreatedAt)
                .FirstOrDefault();
        }

        return candidates
            .OrderBy(chapter => LanguagePreferences.IndexOf(preferredLanguages, chapter.Language))
            .ThenBy(chapter => chapter.ChapterNumber, ChapterNumberComparer.Instance)
            .ThenBy(chapter => chapter.CreatedAt)
            .FirstOrDefault();
    }

    private static bool IsLaterChapter(string candidate, string reference) =>
        ChapterNumberComparer.TryCompareNumericParts(candidate, reference, out var comparison) && comparison > 0;

    private static bool IsEarlierChapter(string candidate, string reference) =>
        ChapterNumberComparer.TryCompareNumericParts(candidate, reference, out var comparison) && comparison < 0;

    private static bool IsSameOrLaterChapter(string candidate, string reference) =>
        ChapterNumberComparer.TryCompareNumericParts(candidate, reference, out var comparison) && comparison >= 0;

    private static bool TryGetChapterParts(string value, out int[] parts)
    {
        var numeric = new string((value ?? "")
            .SkipWhile(character => !char.IsDigit(character))
            .TakeWhile(character => char.IsDigit(character) || character is '.' or ',')
            .ToArray());

        parts = numeric
            .Replace(',', '.')
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : -1)
            .ToArray();
        return parts.Length > 0 && parts.All(part => part >= 0);
    }

    private static decimal? ParseChapterNumber(string value)
    {
        var normalized = new string((value ?? "")
            .SkipWhile(character => !char.IsDigit(character))
            .TakeWhile(character => char.IsDigit(character) || character is '.' or ',')
            .ToArray())
            .Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private MangaSeries CreateCachedSeries(MangaEntry entry, string mangaDexId) => new()
    {
        Title = entry.Title,
        Description = entry.Description,
        CoverUrl = entry.CoverUrl,
        Status = entry.PublishingStatus,
        Source = MangaDexCacheSource,
        ExternalId = mangaDexId
    };

    private string? GetReaderRoot(string source) => source switch
    {
        "local" => Path.GetFullPath(options.Value.LibraryPath),
        MangaDexCacheSource => Path.GetFullPath(options.Value.MangaDexCachePath),
        _ => null
    };

    private bool HasReadableCachedArchive(MangaChapter chapter, string mangaDexId)
    {
        try
        {
            var root = GetReaderRoot(MangaDexCacheSource);
            if (root is null)
            {
                return false;
            }

            var qualityFolder = string.Equals(chapter.ImageQuality, "data-saver", StringComparison.OrdinalIgnoreCase) ? "data-saver" : "";
            var archivePath = Path.GetFullPath(Path.Combine(root, "mangadex", qualityFolder, mangaDexId, $"{chapter.SourceId}.cbz"));
            return archivePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && archives.CountPages(archivePath) > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private Task RecordArchiveRecoveryAsync(string mangaDexId, string chapterSourceId, string action, CancellationToken cancellationToken) =>
        archiveTelemetry?.RecordAsync(mangaDexId, chapterSourceId, action, cancellationToken) ?? Task.CompletedTask;

    private static string GetMangaDexId(MangaEntry entry) => entry.MangaDexId;

    private static string NormalizeImageQuality(string? imageQuality) =>
        string.Equals(imageQuality, "data-saver", StringComparison.OrdinalIgnoreCase) ? "data-saver" : "original";

    private static string NormalizeLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();

    private static bool IsChapterForEntry(MangaChapter chapter, MangaEntry entry) =>
        (entry.LocalSeriesId is not null && chapter.SeriesId == entry.LocalSeriesId)
        || (chapter.Series is { Source: MangaDexCacheSource } series
            && string.Equals(series.ExternalId, GetMangaDexId(entry), StringComparison.Ordinal));

    public sealed class NoNextMangaDexChapterException : Exception
    {
    }

    public sealed class MangaDexUnavailableException : Exception
    {
    }

    public sealed class MangaCompletedException : Exception
    {
    }

    public sealed class MangaDexLanguageFallbackRequiredException(List<string> languages) : Exception
    {
        public List<string> Languages { get; } = languages;
    }

    public sealed class MangaDexClosestChapterConfirmationRequiredException(
        string requestedChapter,
        string matchedChapter,
        string language) : Exception
    {
        public ReaderChapterMatch ChapterMatch { get; } = new(requestedChapter, matchedChapter, language);
    }

    public sealed class MangaDexChapterJumpConfirmationRequiredException(
        string currentChapter,
        string nextChapter,
        string language,
        List<string> alternativeLanguages) : Exception
    {
        public ReaderChapterJump ChapterJump { get; } = new(currentChapter, nextChapter, language, alternativeLanguages);
    }
}

public sealed record ReadOptions(
    Guid Id,
    string Title,
    bool HasMangaDex,
    string FallbackReaderUrl,
    bool HasLocal,
    string LocalReaderUrl);
