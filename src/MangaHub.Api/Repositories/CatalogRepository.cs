using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class CatalogRepository(MangaHubDbContext db)
{
    public Task<MangaEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.MangaEntries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<MangaEntry?> GetByIdNoTrackingAsync(Guid id, CancellationToken cancellationToken) =>
        db.MangaEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<MangaEntry?> FindByMangaDexIdAsync(string mangaDexId, CancellationToken cancellationToken) =>
        db.MangaEntries.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(x => x.MangaDexId == mangaDexId, cancellationToken);

    public Task<MangaEntry?> FindByMangaDexUrlAsync(string url, CancellationToken cancellationToken) =>
        db.MangaEntries.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(x => x.MangaDexUrl == url, cancellationToken);

    public Task<MangaEntry?> FindByTitleAsync(string title, CancellationToken cancellationToken) =>
        db.MangaEntries.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(x => EF.Functions.ILike(x.Title, title), cancellationToken);

    public Task<bool> IsInUserShelfAsync(Guid userId, Guid mangaEntryId, CancellationToken cancellationToken) =>
        db.UserMangaEntries.AnyAsync(x => x.UserId == userId && x.MangaEntryId == mangaEntryId, cancellationToken);

    public async Task<List<CatalogMangaResponse>> SearchAsync(Guid userId, string? queryText, CancellationToken cancellationToken)
    {
        var shelfIds = db.UserMangaEntries
            .Where(x => x.UserId == userId)
            .Select(x => x.MangaEntryId);

        var query = db.MangaEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(queryText))
        {
            query = query.Where(x => EF.Functions.ILike(x.Title, $"%{queryText}%") || EF.Functions.ILike(x.Authors, $"%{queryText}%"));
        }

        return await query
            .OrderBy(x => x.Title)
            .Select(x => new CatalogMangaResponse(
                x.Id,
                x.Title,
                x.Authors,
                x.Category,
                x.Description,
                x.CoverUrl,
                x.OpenLibraryKey,
                x.FirstPublishYear,
                x.MetadataSource,
                x.MyAnimeListId,
                x.MediaType,
                x.PublishingStatus,
                x.ChapterCount,
                x.VolumeCount,
                x.MangaDexUrl,
                x.MangaDexId,
                x.MangaDexLastSyncedAt,
                x.LocalSeriesId,
                db.Series
                    .Where(series => series.Source == "mangadex-cache" && series.ExternalId == x.MangaDexId)
                    .SelectMany(series => series.Chapters)
                    .Count(),
                shelfIds.Contains(x.Id)))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(MangaEntry manga, CancellationToken cancellationToken)
    {
        db.MangaEntries.Add(manga);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
