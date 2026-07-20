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
                : $"/reader/{localFirstChapter.Value.Id}/{localFirstChapter.Value.PageCount}?entryId={entry.Id}&chapter={Uri.EscapeDataString(localFirstChapter.Value.ChapterNumber)}");
    }

    public async Task<ReaderLaunchResponse?> PrepareMangaDexChapterAsync(
        Guid userId,
        Guid entryId,
        Guid? afterCachedChapterId,
        CancellationToken cancellationToken)
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

        var cachedSeries = await series.GetBySourceAndExternalIdAsync(MangaDexCacheSource, mangaDexId, cancellationToken);
        var isNewCachedSeries = cachedSeries is null;
        MangaChapter? cachedChapter = null;
        MangaSourceChapter? sourceChapter = null;

        if (afterCachedChapterId is not null)
        {
            var current = await series.GetChapterWithSeriesAsync(afterCachedChapterId.Value, cancellationToken);
            if (current?.Series is null
                || !string.Equals(current.Series.Source, MangaDexCacheSource, StringComparison.Ordinal)
                || !string.Equals(current.Series.ExternalId, mangaDexId, StringComparison.Ordinal))
            {
                return null;
            }

            sourceChapter = await FindNextMangaDexChapterAsync(mangaDexId, current.SourceId, cancellationToken);
            if (sourceChapter is null)
            {
                return null;
            }

            cachedChapter = cachedSeries?.Chapters.FirstOrDefault(chapter => chapter.SourceId == sourceChapter.Id);
        }
        else if (cachedSeries is not null && !string.IsNullOrWhiteSpace(shelfEntry.CurrentChapter))
        {
            cachedChapter = cachedSeries.Chapters
                .OrderBy(chapter => chapter.CreatedAt)
                .FirstOrDefault(chapter => string.Equals(chapter.ChapterNumber, shelfEntry.CurrentChapter, StringComparison.OrdinalIgnoreCase));
        }

        if (cachedChapter is null)
        {
            var mangaDex = sources.Get("mangadex");
            sourceChapter ??= SelectCurrentMangaDexChapter(
                await mangaDex.GetChaptersAsync(mangaDexId, cancellationToken),
                shelfEntry.CurrentChapter);
            if (sourceChapter is null)
            {
                return null;
            }

            var pages = await mangaDex.GetPagesAsync(sourceChapter.Id, cancellationToken);
            var cachedArchive = await mangaDexCache.EnsureCachedAsync(mangaDexId, sourceChapter.Id, pages, cancellationToken);
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

        shelfEntry.CurrentChapter = cachedChapter.ChapterNumber;
        if (string.Equals(shelfEntry.ReadingStatus, "planned", StringComparison.OrdinalIgnoreCase))
        {
            shelfEntry.ReadingStatus = "reading";
        }
        shelfEntry.UpdatedAt = DateTimeOffset.UtcNow;

        await shelf.SaveChangesAsync(cancellationToken);
        return new ReaderLaunchResponse(
            $"/reader/{cachedChapter.Id}/{cachedChapter.PageCount}?entryId={entry.Id}&chapter={Uri.EscapeDataString(cachedChapter.ChapterNumber)}",
            cachedChapter.ChapterNumber,
            cachedChapter.PageCount);
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

    private static string GetMangaDexId(MangaEntry entry) =>
        string.IsNullOrWhiteSpace(entry.MangaDexId) ? TextRules.ExtractMangaDexId(entry.MangaDexUrl) : entry.MangaDexId;
}

public sealed record ReadOptions(
    Guid Id,
    string Title,
    bool HasMangaDex,
    string MangaDexUrl,
    bool HasLocal,
    string LocalReaderUrl);
