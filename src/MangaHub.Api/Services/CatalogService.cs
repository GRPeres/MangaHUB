using MangaHub.Api.Common;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;

namespace MangaHub.Api.Services;

public sealed class CatalogService(
    CatalogRepository catalog,
    IOpenLibraryClient openLibrary,
    MangaDexCatalogMatchService mangaDexMatches,
    MangaUpdatesCatalogMatchService mangaUpdatesMatches)
{
    public Task<List<CatalogMangaResponse>> SearchAsync(Guid userId, string? query, CancellationToken cancellationToken) =>
        catalog.SearchAsync(userId, query, cancellationToken);

    public async Task<CatalogMangaResponse> CreateAsync(Guid currentUserId, MangaEntryRequest entry, CancellationToken cancellationToken)
    {
        var details = string.IsNullOrWhiteSpace(entry.OpenLibraryKey)
            ? null
            : await openLibrary.GetWorkAsync(entry.OpenLibraryKey.Trim(), cancellationToken);
        var readerLinks = await ResolveReaderLinksAsync(entry, cancellationToken);
        var mangaUpdatesId = await ResolveMangaUpdatesIdAsync(entry.MangaUpdatesId, entry.Title, entry.MediaType, entry.FirstPublishYear, cancellationToken);

        var manga = new MangaEntry
        {
            CreatedByUserId = currentUserId,
            Title = entry.Title.Trim(),
            Authors = entry.Authors.Trim(),
            Category = TextRules.FirstNonEmpty(entry.Category, details?.Category),
            Description = TextRules.FirstNonEmpty(entry.Description, details?.Description),
            CoverUrl = entry.CoverUrl.Trim(),
            MetadataSource = entry.MetadataSource.Trim(),
            MyAnimeListId = entry.MyAnimeListId.Trim(),
            OpenLibraryKey = entry.OpenLibraryKey.Trim(),
            FirstPublishYear = entry.FirstPublishYear,
            MediaType = entry.MediaType.Trim(),
            PublishingStatus = entry.PublishingStatus.Trim(),
            ChapterCount = entry.ChapterCount,
            VolumeCount = entry.VolumeCount,
            MangaDexId = readerLinks.MangaDexId,
            FallbackReaderUrl = readerLinks.FallbackReaderUrl,
            ReaderPreference = NormalizeReaderPreference(entry.ReaderPreference),
            MangaUpdatesId = mangaUpdatesId,
            MangaUpdatesLastMatchAttemptAt = DateTimeOffset.UtcNow,
            LocalSeriesId = entry.LocalSeriesId
        };

        await catalog.AddAsync(manga, cancellationToken);
        return ApiMapping.ToCatalogMangaResponse(manga, false);
    }

    public async Task<CatalogMangaResponse?> UpdateAsync(Guid currentUserId, Guid entryId, MangaEntryRequest entry, CancellationToken cancellationToken)
    {
        var manga = await catalog.GetByIdAsync(entryId, cancellationToken);
        if (manga is null)
        {
            return null;
        }

        manga.Title = entry.Title.Trim();
        manga.Authors = entry.Authors.Trim();
        manga.Category = entry.Category.Trim();
        manga.Description = entry.Description.Trim();
        manga.CoverUrl = entry.CoverUrl.Trim();
        manga.MetadataSource = entry.MetadataSource.Trim();
        manga.MyAnimeListId = entry.MyAnimeListId.Trim();
        manga.OpenLibraryKey = entry.OpenLibraryKey.Trim();
        manga.FirstPublishYear = entry.FirstPublishYear;
        manga.MediaType = entry.MediaType.Trim();
        manga.PublishingStatus = entry.PublishingStatus.Trim();
        manga.ChapterCount = entry.ChapterCount;
        manga.VolumeCount = entry.VolumeCount;
        var readerLinks = await ResolveReaderLinksAsync(entry, cancellationToken);
        manga.MangaDexId = readerLinks.MangaDexId;
        manga.FallbackReaderUrl = readerLinks.FallbackReaderUrl;
        manga.ReaderPreference = NormalizeReaderPreference(entry.ReaderPreference);
        manga.MangaUpdatesId = await ResolveMangaUpdatesIdAsync(
            entry.MangaUpdatesId,
            manga.Title,
            manga.MediaType,
            manga.FirstPublishYear,
            cancellationToken);
        manga.MangaUpdatesLastMatchAttemptAt = DateTimeOffset.UtcNow;
        manga.LocalSeriesId = entry.LocalSeriesId;
        manga.UpdatedAt = DateTimeOffset.UtcNow;

        await catalog.SaveChangesAsync(cancellationToken);
        var isInShelf = await catalog.IsInUserShelfAsync(currentUserId, manga.Id, cancellationToken);
        return ApiMapping.ToCatalogMangaResponse(manga, isInShelf);
    }

    private async Task<ReaderLinks> ResolveReaderLinksAsync(MangaEntryRequest entry, CancellationToken cancellationToken)
    {
        var mangaDexId = NormalizeMangaDexId(entry.MangaDexId);
        var fallbackReaderUrl = entry.FallbackReaderUrl.Trim();
        if (!string.IsNullOrWhiteSpace(mangaDexId)
            || !string.Equals(entry.MetadataSource, "myanimelist", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(entry.MyAnimeListId))
        {
            return new ReaderLinks(mangaDexId, fallbackReaderUrl);
        }

        var match = await mangaDexMatches.FindAsync(entry.MyAnimeListId, entry.Title, cancellationToken);
        mangaDexId = match?.Id ?? "";
        return new ReaderLinks(mangaDexId, fallbackReaderUrl);
    }

    private async Task<string> ResolveMangaUpdatesIdAsync(
        string requestedId,
        string title,
        string mediaType,
        int? firstPublishYear,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedId))
        {
            return requestedId.Trim();
        }

        var match = await mangaUpdatesMatches.FindAsync(title, mediaType, firstPublishYear, cancellationToken);
        return match?.Id ?? "";
    }

    private static string NormalizeMangaDexId(string value)
    {
        var trimmed = value.Trim();
        if (Guid.TryParse(trimmed, out var id))
        {
            return id.ToString();
        }

        return TextRules.ExtractMangaDexId(trimmed);
    }

    private static string NormalizeReaderPreference(string value) =>
        ReaderPreference.Normalize(value);

    private sealed record ReaderLinks(string MangaDexId, string FallbackReaderUrl);
}
