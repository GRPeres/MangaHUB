using MangaHub.Api.Common;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;

namespace MangaHub.Api.Services;

public sealed class CatalogService(CatalogRepository catalog, IOpenLibraryClient openLibrary)
{
    public Task<List<CatalogMangaResponse>> SearchAsync(Guid userId, string? query, CancellationToken cancellationToken) =>
        catalog.SearchAsync(userId, query, cancellationToken);

    public async Task<CatalogMangaResponse> CreateAsync(Guid currentUserId, MangaEntryRequest entry, CancellationToken cancellationToken)
    {
        var details = string.IsNullOrWhiteSpace(entry.OpenLibraryKey)
            ? null
            : await openLibrary.GetWorkAsync(entry.OpenLibraryKey.Trim(), cancellationToken);

        var manga = new MangaEntry
        {
            CreatedByUserId = currentUserId,
            Title = entry.Title.Trim(),
            Authors = entry.Authors.Trim(),
            Category = TextRules.FirstNonEmpty(entry.Category, details?.Category),
            Description = TextRules.FirstNonEmpty(entry.Description, details?.Description),
            CoverUrl = entry.CoverUrl.Trim(),
            OpenLibraryKey = entry.OpenLibraryKey.Trim(),
            FirstPublishYear = entry.FirstPublishYear,
            MangaDexUrl = entry.MangaDexUrl.Trim(),
            MangaDexId = TextRules.ExtractMangaDexId(entry.MangaDexUrl),
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
        manga.OpenLibraryKey = entry.OpenLibraryKey.Trim();
        manga.FirstPublishYear = entry.FirstPublishYear;
        manga.MangaDexUrl = entry.MangaDexUrl.Trim();
        manga.MangaDexId = TextRules.ExtractMangaDexId(entry.MangaDexUrl);
        manga.LocalSeriesId = entry.LocalSeriesId;
        manga.UpdatedAt = DateTimeOffset.UtcNow;

        await catalog.SaveChangesAsync(cancellationToken);
        var isInShelf = await catalog.IsInUserShelfAsync(currentUserId, manga.Id, cancellationToken);
        return ApiMapping.ToCatalogMangaResponse(manga, isInShelf);
    }
}
