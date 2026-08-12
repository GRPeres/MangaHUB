using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class ShelfRepository(MangaHubDbContext db)
{
    public async Task<List<MangaEntryResponse>> ListEntriesAsync(Guid userId, string? status, IReadOnlyList<string> preferredLanguages, int offset, int limit, CancellationToken cancellationToken)
    {
        var languageCodes = preferredLanguages.ToArray();
        var query = db.UserMangaEntries.AsNoTracking()
            .Where(x => x.UserId == userId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.ReadingStatus == status);
        }

        var entries = await query
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
                    .Where(latest => latest.MangaEntryId == x.MangaEntryId && languageCodes.Contains(latest.Language))
                    .Select(latest => (decimal?)latest.LatestChapter)
                    .Max(),
                x.IsRead))
            .ToListAsync(cancellationToken);

        // Shelf ordering depends on the user's language-specific release progress, so sort the
        // projected records before sending a compact page to the client.
        return entries
            .OrderBy(DisplayRank)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    private static int DisplayRank(MangaEntryResponse entry)
    {
        if (IsReadingWithNewChapters(entry)) return 0;
        if (NeedsManualReleaseCheck(entry)) return 1;

        return (entry.ReadingStatus ?? "").ToLowerInvariant() switch
        {
            "planned" => 2,
            "reading" => 3,
            "paused" => 4,
            "dropped" => 5,
            "done" => 6,
            _ => 5
        };
    }

    private static bool IsReadingWithNewChapters(MangaEntryResponse entry) =>
        IsActivelyTracked(entry)
        && !string.IsNullOrWhiteSpace(entry.MangaDexId)
        && entry.MangaDexPreferredLanguageLatestChapter is not null
        && TryGetCurrentChapterNumber(entry.CurrentChapter, out var currentChapter)
        && (entry.MangaDexPreferredLanguageLatestChapter.Value > currentChapter
            || (entry.MangaDexPreferredLanguageLatestChapter.Value == currentChapter && !entry.IsRead));

    private static bool NeedsManualReleaseCheck(MangaEntryResponse entry) =>
        IsActivelyTracked(entry)
        && string.IsNullOrWhiteSpace(entry.MangaDexId);

    private static bool IsActivelyTracked(MangaEntryResponse entry) =>
        string.Equals(entry.ReadingStatus, "reading", StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry.ReadingStatus, "paused", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetCurrentChapterNumber(string currentChapter, out decimal chapter)
    {
        chapter = 0;
        if (string.IsNullOrWhiteSpace(currentChapter)) return false;

        var chapterText = new string(currentChapter
            .SkipWhile(value => !char.IsDigit(value))
            .TakeWhile(value => char.IsDigit(value) || value is '.' or ',')
            .ToArray())
            .Replace(',', '.');
        return decimal.TryParse(chapterText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out chapter);
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
