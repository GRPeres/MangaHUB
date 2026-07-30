using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class ShelfRepository(MangaHubDbContext db)
{
    public async Task<List<MangaEntryResponse>> ListEntriesAsync(Guid userId, string? status, string preferredLanguage, CancellationToken cancellationToken)
    {
        var query = db.UserMangaEntries.AsNoTracking()
            .Include(x => x.MangaEntry)
            .Where(x => x.UserId == userId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.ReadingStatus == status);
        }

        return await query
            .OrderBy(x => x.ReadingStatus)
            .ThenBy(x => x.MangaEntry!.Title)
            .Select(x => new MangaEntryResponse(
                x.MangaEntry!.Id,
                x.MangaEntry.Title,
                x.MangaEntry.Authors,
                x.MangaEntry.Category,
                x.MangaEntry.Description,
                x.MangaEntry.CoverUrl,
                x.MangaEntry.OpenLibraryKey,
                x.MangaEntry.FirstPublishYear,
                x.MangaEntry.MetadataSource,
                x.MangaEntry.MyAnimeListId,
                x.MangaEntry.MediaType,
                x.MangaEntry.PublishingStatus,
                x.MangaEntry.ChapterCount,
                x.MangaEntry.VolumeCount,
                x.ReadingStatus,
                x.MangaEntry.MangaDexId,
                x.MangaEntry.MangaDexLatestChapter,
                x.MangaEntry.MangaDexLastSyncedAt,
                x.MangaEntry.MangaUpdatesId,
                x.MangaEntry.MangaUpdatesLatestChapter,
                x.MangaEntry.MangaUpdatesStatus,
                x.MangaEntry.MangaUpdatesCompleted,
                x.MangaEntry.MangaUpdatesLastSyncedAt,
                x.MangaEntry.LocalSeriesId,
                x.CurrentChapter,
                x.Score,
                x.Category,
                x.Summary,
                x.Notes,
                x.MangaEntry.FallbackReaderUrl,
                x.MangaEntry.ReaderPreference,
                db.MangaDexLanguageLatestChapters
                    .Where(latest => latest.MangaEntryId == x.MangaEntryId && latest.Language == preferredLanguage)
                    .Select(latest => (decimal?)latest.LatestChapter)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public Task<UserMangaEntry?> GetAsync(Guid userId, Guid mangaEntryId, CancellationToken cancellationToken) =>
        db.UserMangaEntries.FirstOrDefaultAsync(x => x.UserId == userId && x.MangaEntryId == mangaEntryId, cancellationToken);

    public Task<UserMangaEntry?> GetWithMangaAsync(Guid userId, Guid mangaEntryId, CancellationToken cancellationToken) =>
        db.UserMangaEntries.Include(x => x.MangaEntry)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.MangaEntryId == mangaEntryId, cancellationToken);

    public Task<UserMangaEntry?> GetReadShelfAsync(Guid userId, Guid mangaEntryId, CancellationToken cancellationToken) =>
        db.UserMangaEntries.AsNoTracking()
            .Include(x => x.MangaEntry)
            .FirstOrDefaultAsync(x => x.MangaEntryId == mangaEntryId && x.UserId == userId, cancellationToken);

    public void Add(UserMangaEntry shelf) => db.UserMangaEntries.Add(shelf);

    public void Remove(UserMangaEntry shelf) => db.UserMangaEntries.Remove(shelf);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);

    public void ClearTracking() => db.ChangeTracker.Clear();
}
