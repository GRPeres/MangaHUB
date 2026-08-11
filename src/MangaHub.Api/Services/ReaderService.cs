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
    MangaSourceRegistry sources)
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
        string? requestedChapter = null)
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

            sourceChapter = afterCachedChapterId is not null
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
                if (afterCachedChapterId is not null && IsPublishingComplete(entry))
                {
                    await MarkShelfEntryDoneAsync(shelfEntry, cancellationToken);
                    throw new MangaCompletedException();
                }
                return null;
            }

            if (afterCachedChapterId is not null
                && !allowChapterJump
                && IsChapterJump(current.ChapterNumber, sourceChapter.Number))
            {
                throw new MangaDexChapterJumpConfirmationRequiredException(
                    current.ChapterNumber,
                    sourceChapter.Number,
                    NormalizeLanguage(sourceChapter.Language),
                    await FindCloserNextChapterLanguagesAsync(mangaDexId, current.ChapterNumber, sourceChapter.Number, preferredLanguages, cancellationToken));
            }

            cachedChapter = cachedSeries?.Chapters.FirstOrDefault(chapter => chapter.SourceId == sourceChapter.Id);
        }
        else if (shelfEntry.IsRead && !string.IsNullOrWhiteSpace(shelfEntry.CurrentChapter))
        {
            sourceChapter = await FindNextMangaDexChapterAfterNumberAsync(mangaDexId, shelfEntry.CurrentChapter, preferredLanguages, cancellationToken);
            if (sourceChapter is null)
            {
                var availableLanguages = await FindAvailableLanguagesAfterNumberAsync(mangaDexId, shelfEntry.CurrentChapter, preferredLanguages, cancellationToken);
                if (!allowLanguageFallback && availableLanguages.Count > 0)
                {
                    throw new MangaDexLanguageFallbackRequiredException(availableLanguages);
                }
                await RecordCompletedMangaDexChapterAsync(entry, shelfEntry.CurrentChapter, cancellationToken);
                if (IsPublishingComplete(entry))
                {
                    await MarkShelfEntryDoneAsync(shelfEntry, cancellationToken);
                    throw new MangaCompletedException();
                }
                throw new NoNextMangaDexChapterException();
            }

            if (!allowChapterJump && IsChapterJump(shelfEntry.CurrentChapter, sourceChapter.Number))
            {
                throw new MangaDexChapterJumpConfirmationRequiredException(
                    shelfEntry.CurrentChapter,
                    sourceChapter.Number,
                    NormalizeLanguage(sourceChapter.Language),
                    await FindCloserNextChapterLanguagesAsync(mangaDexId, shelfEntry.CurrentChapter, sourceChapter.Number, preferredLanguages, cancellationToken));
            }

            cachedChapter = cachedSeries?.Chapters.FirstOrDefault(chapter => chapter.SourceId == sourceChapter.Id);
        }
        else if (cachedSeries is not null && !string.IsNullOrWhiteSpace(shelfEntry.CurrentChapter))
        {
            cachedChapter = cachedSeries.Chapters
                .OrderBy(chapter => LanguagePreferences.IndexOf(preferredLanguages, chapter.Language))
                .ThenBy(chapter => chapter.CreatedAt)
                .FirstOrDefault(chapter => HasExactChapter(chapter.ChapterNumber, shelfEntry.CurrentChapter));
        }

        if (cachedChapter is not null && !HasReadableCachedArchive(cachedChapter, mangaDexId))
        {
            progress?.Report(new ReaderPreparationProgress("Refreshing an unreadable local chapter", 12));
            await mangaDexCache.DeleteAsync(mangaDexId, cachedChapter.SourceId, cancellationToken);
            cachedChapter = null;
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
                return null;
            }

            cachedChapter ??= cachedSeries?.Chapters.FirstOrDefault(chapter => chapter.SourceId == sourceChapter.Id);
            if (isInitialTrackedChapterSelection
                && string.Equals(shelfEntry.ReadingStatus, "planned", StringComparison.OrdinalIgnoreCase)
                && !allowChapterJump
                && !HasExactChapter(sourceChapter.Number, "1"))
            {
                throw new MangaDexChapterJumpConfirmationRequiredException(
                    "1",
                    sourceChapter.Number,
                    NormalizeLanguage(sourceChapter.Language),
                    await FindCloserNextChapterLanguagesAsync(mangaDexId, "1", sourceChapter.Number, preferredLanguages, cancellationToken));
            }
            if (isInitialTrackedChapterSelection
                && !allowLanguageFallback
                && !string.IsNullOrWhiteSpace(shelfEntry.CurrentChapter)
                && !HasExactChapter(sourceChapter.Number, shelfEntry.CurrentChapter))
            {
                throw new MangaDexClosestChapterConfirmationRequiredException(
                    shelfEntry.CurrentChapter,
                    sourceChapter.Number,
                    NormalizeLanguage(sourceChapter.Language));
            }

            progress?.Report(new ReaderPreparationProgress("Loading the MangaDex page list", 20));
            var pages = await mangaDex.GetPagesAsync(sourceChapter.Id, cancellationToken);
            var cachedArchive = await mangaDexCache.EnsureCachedAsync(mangaDexId, sourceChapter.Id, pages, cancellationToken, progress);
            cachedSeries ??= CreateCachedSeries(entry, mangaDexId);
            if (isNewCachedSeries)
            {
                series.AddSeries(cachedSeries);
            }

            cachedChapter = cachedSeries.Chapters.FirstOrDefault(chapter => chapter.SourceId == sourceChapter.Id);
            if (cachedChapter is null)
            {
                cachedChapter = new MangaChapter
                {
                    Series = cachedSeries,
                    ChapterNumber = sourceChapter.Number,
                    Language = sourceChapter.Language,
                    Title = sourceChapter.Title,
                    SourceId = sourceChapter.Id,
                    PageCount = cachedArchive.PageCount,
                    FileHash = cachedArchive.FileHash
                };
                cachedSeries.Chapters.Add(cachedChapter);
                series.AddChapter(cachedChapter);
            }
            else
            {
                cachedChapter.ChapterNumber = sourceChapter.Number;
                cachedChapter.Language = sourceChapter.Language;
                cachedChapter.Title = sourceChapter.Title;
                cachedChapter.PageCount = cachedArchive.PageCount;
                cachedChapter.FileHash = cachedArchive.FileHash;
            }
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
            $"/reader/{cachedChapter.Id}/{cachedChapter.PageCount}?entryId={entry.Id}&chapter={Uri.EscapeDataString(cachedChapter.ChapterNumber)}&source=mangadex&language={Uri.EscapeDataString(resolvedLanguage)}{ReaderModeQuery(entry)}",
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
            requestedChapter: null);

    public Task PrefetchNextMangaDexChapterAsync(Guid userId, Guid entryId, Guid afterCachedChapterId, CancellationToken cancellationToken) =>
        PrefetchNextMangaDexChapterAsync(userId, entryId, afterCachedChapterId, "en", cancellationToken);

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
        var exactIndex = chapters.Select((chapter, index) => new { chapter, index })
            .FirstOrDefault(item => string.Equals(item.chapter.Number, currentChapterNumber, StringComparison.OrdinalIgnoreCase))?.index;
        if (exactIndex is not null)
        {
            return exactIndex.Value + 1 < chapters.Count ? chapters[exactIndex.Value + 1] : null;
        }

        var currentNumber = ParseChapterNumber(currentChapterNumber);
        return currentNumber is null
            ? null
            : chapters.FirstOrDefault(chapter => ParseChapterNumber(chapter.Number) is { } chapterNumber && chapterNumber > currentNumber);
    }

    private async Task<MangaSourceChapter?> FindNextMangaDexChapterAfterNumberAsync(string mangaDexId, string currentChapterNumber, IReadOnlyList<string> preferredLanguages, CancellationToken cancellationToken)
    {
        foreach (var language in preferredLanguages)
        {
            var chapter = await FindNextMangaDexChapterAfterNumberAsync(mangaDexId, currentChapterNumber, language, cancellationToken);
            if (chapter is not null) return chapter;
        }

        return null;
    }

    private async Task<List<string>> FindAvailableLanguagesAfterNumberAsync(string mangaDexId, string currentChapterNumber, IReadOnlyList<string> preferredLanguages, CancellationToken cancellationToken)
    {
        var currentNumber = ParseChapterNumber(currentChapterNumber);
        if (currentNumber is null)
        {
            return [];
        }

        return (await sources.Get("mangadex").GetChaptersAsync(mangaDexId, null, cancellationToken))
            .Where(chapter => !LanguagePreferences.Contains(preferredLanguages, chapter.Language))
            .Where(chapter => ParseChapterNumber(chapter.Number) is { } number && number > currentNumber)
            .Select(chapter => NormalizeLanguage(chapter.Language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<MangaSourceChapter?> FindPreviousMangaDexChapterBeforeNumberAsync(string mangaDexId, string currentChapterNumber, string language, CancellationToken cancellationToken)
    {
        var chapters = await sources.Get("mangadex").GetChaptersAsync(mangaDexId, language, cancellationToken);
        var currentNumber = ParseChapterNumber(currentChapterNumber);
        return currentNumber is null
            ? null
            : chapters
                .Select(chapter => new { Chapter = chapter, Number = ParseChapterNumber(chapter.Number) })
                .Where(item => item.Number is not null && item.Number < currentNumber)
                .OrderByDescending(item => item.Number)
                .Select(item => item.Chapter)
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
        var currentNumber = ParseChapterNumber(currentChapterNumber);
        if (currentNumber is null)
        {
            return [];
        }

        return (await sources.Get("mangadex").GetChaptersAsync(mangaDexId, null, cancellationToken))
            .Where(chapter => !LanguagePreferences.Contains(preferredLanguages, chapter.Language))
            .Where(chapter => ParseChapterNumber(chapter.Number) is { } number && number < currentNumber)
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
        var currentNumber = ParseChapterNumber(currentChapterNumber);
        var proposedNumber = ParseChapterNumber(proposedChapterNumber);
        if (currentNumber is null || proposedNumber is null)
        {
            return [];
        }

        return (await sources.Get("mangadex").GetChaptersAsync(mangaDexId, null, cancellationToken))
            .Where(chapter => !LanguagePreferences.Contains(preferredLanguages, chapter.Language))
            .Select(chapter => new { Language = NormalizeLanguage(chapter.Language), Number = ParseChapterNumber(chapter.Number) })
            .Where(item => item.Number is not null && item.Number > currentNumber && item.Number < proposedNumber)
            .Select(item => item.Language)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
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
            var chapters = await sources.Get("mangadex").GetChaptersAsync(mangaDexId, language, cancellationToken);
            firstAvailable ??= chapters;
            if (string.IsNullOrWhiteSpace(currentChapterNumber) || HasExactChapter(chapters, currentChapterNumber)) return chapters;
        }

        return firstAvailable ?? [];
    }

    private async Task RecordCompletedMangaDexChapterAsync(MangaEntry entry, string currentChapterNumber, CancellationToken cancellationToken)
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
    }

    private async Task MarkShelfEntryDoneAsync(UserMangaEntry shelfEntry, CancellationToken cancellationToken)
    {
        shelfEntry.ReadingStatus = "done";
        shelfEntry.IsRead = true;
        shelfEntry.UpdatedAt = DateTimeOffset.UtcNow;
        await shelf.SaveChangesAsync(cancellationToken);
        if (usage is not null) await usage.TrackAsync(shelfEntry.UserId, UsageEventTypes.MangaCompleted, shelfEntry.MangaEntryId, cancellationToken);
    }

    private static bool IsPublishingComplete(MangaEntry entry) =>
        entry.MangaUpdatesCompleted == true
        || entry.PublishingStatus.Trim().ToLowerInvariant() is "finished" or "complete" or "completed" or "done" or "ended";

    private static string ReaderModeQuery(MangaEntry entry) => UsesVerticalReader(entry) ? "&vertical=true" : "";

    private static bool UsesVerticalReader(MangaEntry entry) =>
        entry.MediaType.Contains("manhwa", StringComparison.OrdinalIgnoreCase)
        || entry.MediaType.Contains("webtoon", StringComparison.OrdinalIgnoreCase);

    private static MangaSourceChapter? SelectCurrentMangaDexChapter(IReadOnlyList<MangaSourceChapter> chapters, string currentChapter)
    {
        if (string.IsNullOrWhiteSpace(currentChapter))
        {
            return chapters.FirstOrDefault();
        }

        var exact = chapters.FirstOrDefault(chapter => string.Equals(chapter.Number, currentChapter, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var normalized = new string(currentChapter.Where(character => char.IsDigit(character) || character is '.' or ',').ToArray()).Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var currentNumber))
        {
            return null;
        }

        var numberedChapters = chapters
            .Select(chapter => new { Chapter = chapter, Number = ParseChapterNumber(chapter.Number) })
            .Where(item => item.Number is not null)
            .ToList();
        return numberedChapters.FirstOrDefault(item => item.Number == currentNumber)?.Chapter
            ?? numberedChapters.Where(item => item.Number >= currentNumber).OrderBy(item => item.Number).FirstOrDefault()?.Chapter
            ?? numberedChapters.OrderByDescending(item => item.Number).FirstOrDefault()?.Chapter;
    }

    private static bool HasExactChapter(IReadOnlyList<MangaSourceChapter> chapters, string currentChapter) =>
        chapters.Any(chapter => HasExactChapter(chapter.Number, currentChapter));

    private static bool HasExactChapter(string chapterNumber, string currentChapter) =>
        string.Equals(chapterNumber, currentChapter, StringComparison.OrdinalIgnoreCase)
        || (ParseChapterNumber(chapterNumber) is { } parsedChapter
            && ParseChapterNumber(currentChapter) is { } parsedCurrent
            && parsedChapter == parsedCurrent);

    private static bool IsChapterJump(string currentChapter, string nextChapter) =>
        ParseChapterNumber(currentChapter) is { } current
        && ParseChapterNumber(nextChapter) is { } next
        && next - current > 1m;

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

            var archivePath = Path.GetFullPath(Path.Combine(root, "mangadex", mangaDexId, $"{chapter.SourceId}.cbz"));
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

    private static string GetMangaDexId(MangaEntry entry) => entry.MangaDexId;

    private static string NormalizeLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();

    private static bool IsChapterForEntry(MangaChapter chapter, MangaEntry entry) =>
        (entry.LocalSeriesId is not null && chapter.SeriesId == entry.LocalSeriesId)
        || (chapter.Series is { Source: MangaDexCacheSource } series
            && string.Equals(series.ExternalId, GetMangaDexId(entry), StringComparison.Ordinal));

    public sealed class NoNextMangaDexChapterException : Exception
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
