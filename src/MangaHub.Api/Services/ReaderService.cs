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
    IMangaDexChapterCache mangaDexCache,
    IOptions<MangaHubOptions> options,
    MangaSourceRegistry sources)
{
    private const string MangaDexCacheSource = "mangadex-cache";

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
            entry.MangaDexUrl,
            localFirstChapter is not null,
            localFirstChapter is null
                ? ""
                : $"/reader/{localFirstChapter.Value.Id}/{localFirstChapter.Value.PageCount}?entryId={entry.Id}&chapter={Uri.EscapeDataString(localFirstChapter.Value.ChapterNumber)}&source=local");
    }

    public async Task<ReaderLaunchResponse?> PrepareMangaDexChapterAsync(
        Guid userId,
        Guid entryId,
        Guid? afterCachedChapterId,
        Guid? beforeCachedChapterId,
        CancellationToken cancellationToken,
        IProgress<ReaderPreparationProgress>? progress = null,
        bool updateReadingProgress = true)
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
                ? await FindNextMangaDexChapterAsync(mangaDexId, current.SourceId, cancellationToken)
                : await FindPreviousMangaDexChapterAsync(mangaDexId, current.SourceId, cancellationToken);
            if (sourceChapter is null)
            {
                return null;
            }

            cachedChapter = cachedSeries?.Chapters.FirstOrDefault(chapter => chapter.SourceId == sourceChapter.Id);
        }
        else if (shelfEntry.IsRead && !string.IsNullOrWhiteSpace(shelfEntry.CurrentChapter))
        {
            sourceChapter = await FindNextMangaDexChapterAfterNumberAsync(mangaDexId, shelfEntry.CurrentChapter, cancellationToken);
            if (sourceChapter is null)
            {
                await RecordCompletedMangaDexChapterAsync(entry, shelfEntry.CurrentChapter, cancellationToken);
                throw new NoNextMangaDexChapterException();
            }

            cachedChapter = cachedSeries?.Chapters.FirstOrDefault(chapter => chapter.SourceId == sourceChapter.Id);
        }
        else if (cachedSeries is not null && !string.IsNullOrWhiteSpace(shelfEntry.CurrentChapter))
        {
            cachedChapter = cachedSeries.Chapters
                .OrderBy(chapter => chapter.CreatedAt)
                .FirstOrDefault(chapter => string.Equals(chapter.ChapterNumber, shelfEntry.CurrentChapter, StringComparison.OrdinalIgnoreCase));
        }

        if (cachedChapter is not null && !HasReadableCachedArchive(cachedChapter, mangaDexId))
        {
            progress?.Report(new ReaderPreparationProgress("Refreshing an unreadable local chapter", 12));
            await mangaDexCache.DeleteAsync(mangaDexId, cachedChapter.SourceId, cancellationToken);
            cachedChapter = null;
        }

        if (cachedChapter is null)
        {
            var mangaDex = sources.Get("mangadex");
            progress?.Report(new ReaderPreparationProgress("Loading MangaDex chapter list", 8));
            sourceChapter ??= SelectCurrentMangaDexChapter(
                await mangaDex.GetChaptersAsync(mangaDexId, cancellationToken),
                shelfEntry.CurrentChapter);
            if (sourceChapter is null)
            {
                return null;
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
                cachedChapter.Title = sourceChapter.Title;
                cachedChapter.PageCount = cachedArchive.PageCount;
                cachedChapter.FileHash = cachedArchive.FileHash;
            }
        }

        var shouldAdvanceReadingProgress = updateReadingProgress && beforeCachedChapterId is null;
        if (shouldAdvanceReadingProgress)
        {
            shelfEntry.CurrentChapter = cachedChapter.ChapterNumber;
            shelfEntry.IsRead = false;
            if (string.Equals(shelfEntry.ReadingStatus, "planned", StringComparison.OrdinalIgnoreCase))
            {
                shelfEntry.ReadingStatus = "reading";
            }
            shelfEntry.UpdatedAt = DateTimeOffset.UtcNow;

            progress?.Report(new ReaderPreparationProgress("Saving your reading progress", 98));
            await shelf.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await series.SaveChangesAsync(cancellationToken);
        }

        progress?.Report(new ReaderPreparationProgress(
            shouldAdvanceReadingProgress ? "Opening the local reader" : "The chapter is ready", 100));
        return new ReaderLaunchResponse(
            $"/reader/{cachedChapter.Id}/{cachedChapter.PageCount}?entryId={entry.Id}&chapter={Uri.EscapeDataString(cachedChapter.ChapterNumber)}&source=mangadex",
            cachedChapter.ChapterNumber,
            cachedChapter.PageCount);
    }

    public async Task PrefetchNextMangaDexChapterAsync(
        Guid userId,
        Guid entryId,
        Guid afterCachedChapterId,
        CancellationToken cancellationToken) =>
        await PrepareMangaDexChapterAsync(
            userId,
            entryId,
            afterCachedChapterId,
            null,
            cancellationToken,
            updateReadingProgress: false);

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
            || !string.Equals(chapter.ChapterNumber, shelfEntry.CurrentChapter, StringComparison.OrdinalIgnoreCase)
            || !IsChapterForEntry(chapter, shelfEntry.MangaEntry))
        {
            return false;
        }

        shelfEntry.IsRead = true;
        shelfEntry.UpdatedAt = DateTimeOffset.UtcNow;
        await shelf.SaveChangesAsync(cancellationToken);
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

    private async Task<MangaSourceChapter?> FindNextMangaDexChapterAsync(string mangaDexId, string currentChapterId, CancellationToken cancellationToken)
    {
        var chapters = await sources.Get("mangadex").GetChaptersAsync(mangaDexId, cancellationToken);
        var currentIndex = chapters.Select((chapter, index) => new { chapter, index })
            .FirstOrDefault(item => string.Equals(item.chapter.Id, currentChapterId, StringComparison.Ordinal))?.index;
        return currentIndex is null || currentIndex.Value + 1 >= chapters.Count ? null : chapters[currentIndex.Value + 1];
    }

    private async Task<MangaSourceChapter?> FindPreviousMangaDexChapterAsync(string mangaDexId, string currentChapterId, CancellationToken cancellationToken)
    {
        var chapters = await sources.Get("mangadex").GetChaptersAsync(mangaDexId, cancellationToken);
        var currentIndex = chapters.Select((chapter, index) => new { chapter, index })
            .FirstOrDefault(item => string.Equals(item.chapter.Id, currentChapterId, StringComparison.Ordinal))?.index;
        return currentIndex is null || currentIndex.Value == 0 ? null : chapters[currentIndex.Value - 1];
    }

    private async Task<MangaSourceChapter?> FindNextMangaDexChapterAfterNumberAsync(string mangaDexId, string currentChapterNumber, CancellationToken cancellationToken)
    {
        var chapters = await sources.Get("mangadex").GetChaptersAsync(mangaDexId, cancellationToken);
        var exactIndex = chapters.Select((chapter, index) => new { chapter, index })
            .FirstOrDefault(item => string.Equals(item.chapter.Number, currentChapterNumber, StringComparison.OrdinalIgnoreCase))?.index;
        if (exactIndex is not null)
        {
            return exactIndex.Value + 1 < chapters.Count ? chapters[exactIndex.Value + 1] : null;
        }

        return decimal.TryParse(currentChapterNumber, NumberStyles.Number, CultureInfo.InvariantCulture, out var currentNumber)
            ? chapters.FirstOrDefault(chapter => decimal.TryParse(chapter.Number, NumberStyles.Number, CultureInfo.InvariantCulture, out var chapterNumber) && chapterNumber > currentNumber)
            : null;
    }

    private async Task RecordCompletedMangaDexChapterAsync(MangaEntry entry, string currentChapterNumber, CancellationToken cancellationToken)
    {
        var normalizedChapter = currentChapterNumber.Replace(',', '.');
        if (!decimal.TryParse(normalizedChapter, NumberStyles.Number, CultureInfo.InvariantCulture, out var chapterNumber))
        {
            return;
        }

        entry.MangaDexLatestChapter = chapterNumber;
        entry.ChapterCount = (int)Math.Floor(chapterNumber);
        entry.MangaDexLastSyncedAt = DateTimeOffset.UtcNow;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        await shelf.SaveChangesAsync(cancellationToken);
    }

    private static MangaSourceChapter? SelectCurrentMangaDexChapter(IReadOnlyList<MangaSourceChapter> chapters, string currentChapter) =>
        !string.IsNullOrWhiteSpace(currentChapter)
            ? chapters.FirstOrDefault(chapter => string.Equals(chapter.Number, currentChapter, StringComparison.OrdinalIgnoreCase)) ?? chapters.FirstOrDefault()
            : chapters.FirstOrDefault();

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

    private static string GetMangaDexId(MangaEntry entry) =>
        string.IsNullOrWhiteSpace(entry.MangaDexId) ? TextRules.ExtractMangaDexId(entry.MangaDexUrl) : entry.MangaDexId;

    private static bool IsChapterForEntry(MangaChapter chapter, MangaEntry entry) =>
        (entry.LocalSeriesId is not null && chapter.SeriesId == entry.LocalSeriesId)
        || (chapter.Series is { Source: MangaDexCacheSource } series
            && string.Equals(series.ExternalId, GetMangaDexId(entry), StringComparison.Ordinal));

    public sealed class NoNextMangaDexChapterException : Exception
    {
    }
}

public sealed record ReadOptions(
    Guid Id,
    string Title,
    bool HasMangaDex,
    string MangaDexUrl,
    bool HasLocal,
    string LocalReaderUrl);
