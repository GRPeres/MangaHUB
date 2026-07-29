using System.Globalization;
using MangaHub.Api.Common;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using MangaHub.Infrastructure.Sources;

namespace MangaHub.Api.Services;

public sealed class CatalogCacheService(
    CatalogRepository catalog,
    SeriesRepository series,
    IMangaDexChapterCache cache,
    MangaSourceRegistry sources)
{
    private const string CacheSource = "mangadex-cache";

    public async Task<MangaDexCacheResponse?> ListAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var entry = await catalog.GetByIdAsync(entryId, cancellationToken);
        var mangaDexId = entry is null ? "" : GetMangaDexId(entry);
        if (string.IsNullOrWhiteSpace(mangaDexId))
        {
            return null;
        }

        var cachedSeries = await series.GetBySourceAndExternalIdAsync(CacheSource, mangaDexId, cancellationToken);
        var chapters = cachedSeries?.Chapters
            .OrderByDescending(chapter => ParseChapterNumber(chapter.ChapterNumber) ?? decimal.MinValue)
            .ThenByDescending(chapter => chapter.CreatedAt)
            .Select(chapter => new CachedMangaDexChapterResponse(
                chapter.Id,
                chapter.ChapterNumber,
                chapter.Language,
                chapter.Title,
                chapter.PageCount,
                chapter.CreatedAt,
                chapter.SourceId.StartsWith("manual-", StringComparison.Ordinal)))
            .ToList() ?? [];
        return new MangaDexCacheResponse(mangaDexId, chapters);
    }

    public async Task<MangaDexCacheResponse?> DownloadAsync(Guid entryId, CacheMangaDexChapterRequest request, string preferredLanguage, CancellationToken cancellationToken)
    {
        var entry = await catalog.GetByIdAsync(entryId, cancellationToken);
        var mangaDexId = entry is null ? "" : GetMangaDexId(entry);
        if (string.IsNullOrWhiteSpace(mangaDexId) || string.IsNullOrWhiteSpace(request.ChapterNumber))
        {
            return null;
        }

        var source = sources.Get("mangadex");
        var requestedNumber = request.ChapterNumber.Trim();
        var chapters = await source.GetChaptersAsync(mangaDexId, null, cancellationToken);
        var chapter = chapters
            .Where(item => string.Equals(item.Number, requestedNumber, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => string.Equals(item.Language, preferredLanguage, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.Language, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? chapters
                .Where(item => FindNumericChapter([item], requestedNumber) is not null)
                .OrderBy(item => string.Equals(item.Language, preferredLanguage, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(item => item.Language, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        if (chapter is null)
        {
            return null;
        }

        await CacheChapterAsync(entry!, mangaDexId, chapter, cancellationToken);
        return await ListAsync(entryId, cancellationToken);
    }

    public async Task<MangaDexCacheResponse?> ImportAsync(Guid entryId, string chapterNumber, string? title, IFormFile file, CancellationToken cancellationToken)
    {
        var entry = await catalog.GetByIdAsync(entryId, cancellationToken);
        var mangaDexId = entry is null ? "" : GetMangaDexId(entry);
        if (string.IsNullOrWhiteSpace(mangaDexId)
            || string.IsNullOrWhiteSpace(chapterNumber)
            || file.Length == 0
            || !file.FileName.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var manualChapter = new MangaSourceChapter($"manual-{Guid.NewGuid():N}", chapterNumber.Trim(), title?.Trim() ?? "", 0, "manual");
        await using var content = file.OpenReadStream();
        await CacheChapterAsync(entry!, mangaDexId, manualChapter, cancellationToken, content);
        return await ListAsync(entryId, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid entryId, Guid chapterId, CancellationToken cancellationToken)
    {
        var entry = await catalog.GetByIdAsync(entryId, cancellationToken);
        var mangaDexId = entry is null ? "" : GetMangaDexId(entry);
        if (string.IsNullOrWhiteSpace(mangaDexId))
        {
            return false;
        }

        var chapter = await series.GetChapterWithSeriesAsync(chapterId, cancellationToken);
        if (chapter?.Series is null
            || !string.Equals(chapter.Series.Source, CacheSource, StringComparison.Ordinal)
            || !string.Equals(chapter.Series.ExternalId, mangaDexId, StringComparison.Ordinal))
        {
            return false;
        }

        await cache.DeleteAsync(mangaDexId, chapter.SourceId, cancellationToken);
        series.RemoveChapter(chapter);
        await series.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task CacheChapterAsync(
        MangaEntry entry,
        string mangaDexId,
        MangaSourceChapter chapter,
        CancellationToken cancellationToken,
        Stream? importedContent = null)
    {
        var cachedSeries = await series.GetBySourceAndExternalIdAsync(CacheSource, mangaDexId, cancellationToken);
        if (cachedSeries is null)
        {
            cachedSeries = CreateCachedSeries(entry, mangaDexId);
            series.AddSeries(cachedSeries);
        }

        var archive = importedContent is null
            ? await cache.EnsureCachedAsync(mangaDexId, chapter.Id, await sources.Get("mangadex").GetPagesAsync(chapter.Id, cancellationToken), cancellationToken)
            : await cache.ImportAsync(mangaDexId, chapter.Id, importedContent, cancellationToken);
        var cachedChapter = cachedSeries.Chapters.FirstOrDefault(item => item.SourceId == chapter.Id);
        if (cachedChapter is null)
        {
            cachedChapter = new MangaChapter
            {
                Series = cachedSeries,
                ChapterNumber = chapter.Number,
                Language = chapter.Language,
                Title = chapter.Title,
                SourceId = chapter.Id,
                PageCount = archive.PageCount,
                FileHash = archive.FileHash
            };
            cachedSeries.Chapters.Add(cachedChapter);
            series.AddChapter(cachedChapter);
        }
        else
        {
            cachedChapter.ChapterNumber = chapter.Number;
            cachedChapter.Language = chapter.Language;
            cachedChapter.Title = chapter.Title;
            cachedChapter.PageCount = archive.PageCount;
            cachedChapter.FileHash = archive.FileHash;
        }

        await series.SaveChangesAsync(cancellationToken);
    }

    private static MangaSeries CreateCachedSeries(MangaEntry entry, string mangaDexId) => new()
    {
        Title = entry.Title,
        Description = entry.Description,
        CoverUrl = entry.CoverUrl,
        Status = entry.PublishingStatus,
        Source = CacheSource,
        ExternalId = mangaDexId
    };

    private static MangaSourceChapter? FindNumericChapter(IReadOnlyList<MangaSourceChapter> chapters, string requestedNumber)
    {
        if (!decimal.TryParse(requestedNumber, NumberStyles.Number, CultureInfo.InvariantCulture, out var requested))
        {
            return null;
        }

        return chapters.FirstOrDefault(chapter => ParseChapterNumber(chapter.Number) == requested);
    }

    private static decimal? ParseChapterNumber(string value)
    {
        var normalized = new string((value ?? "")
            .Where(character => char.IsDigit(character) || character is '.' or ',')
            .ToArray())
            .Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private static string GetMangaDexId(MangaEntry entry) => entry.MangaDexId;
}
