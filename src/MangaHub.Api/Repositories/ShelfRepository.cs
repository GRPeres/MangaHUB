using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MangaHub.Api.Repositories;

public sealed class ShelfRepository(MangaHubDbContext db)
{
    public bool SupportsTransactions => !string.Equals(
        db.Database.ProviderName,
        "Microsoft.EntityFrameworkCore.InMemory",
        StringComparison.Ordinal);

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        db.Database.BeginTransactionAsync(cancellationToken);

    public async Task<List<MangaEntryResponse>> ListEntriesAsync(Guid userId, string? status, string? section, IReadOnlyList<string> preferredLanguages, DateTimeOffset manualCheckDueBefore, int offset, int limit, CancellationToken cancellationToken)
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
                x.IsRead,
                false,
                x.LastExternalReaderVerifiedAt))
            .ToListAsync(cancellationToken);

        // Shelf ordering depends on the user's language-specific release progress, so sort the
        // projected records before sending a compact page to the client.
        var enrichedEntries = entries
            .Select(entry => entry with { IsManualReleaseCheckDue = NeedsManualReleaseCheck(entry, manualCheckDueBefore) })
            .ToList();

        return FilterBySection(enrichedEntries, section)
            .OrderBy(DisplayRank)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    public async Task<ShelfSectionSummaryResponse> GetSectionSummaryAsync(Guid userId, IReadOnlyList<string> preferredLanguages, DateTimeOffset manualCheckDueBefore, CancellationToken cancellationToken)
    {
        var entries = await ListEntriesAsync(userId, null, null, preferredLanguages, manualCheckDueBefore, 0, int.MaxValue, cancellationToken);
        var newReleases = entries.Count(IsReadingWithNewChapters);
        var untracked = entries.Count(entry => entry.IsManualReleaseCheckDue);

        return new ShelfSectionSummaryResponse(
            newReleases + untracked,
            newReleases,
            untracked,
            entries.Count(entry => HasStatus(entry, "planned")),
            entries.Count(entry => HasStatus(entry, "reading")),
            entries.Count(entry => HasStatus(entry, "paused")),
            entries.Count(entry => HasStatus(entry, "done")),
            entries.Count(entry => HasStatus(entry, "dropped")),
            entries.Count);
    }

    private static IEnumerable<MangaEntryResponse> FilterBySection(IEnumerable<MangaEntryResponse> entries, string? section) =>
        NormalizeSection(section) switch
        {
            "updates" => entries.Where(entry => IsReadingWithNewChapters(entry) || entry.IsManualReleaseCheckDue),
            "planned" => entries.Where(entry => HasStatus(entry, "planned")),
            "reading" => entries.Where(entry => HasStatus(entry, "reading")),
            "paused" => entries.Where(entry => HasStatus(entry, "paused")),
            "done" => entries.Where(entry => HasStatus(entry, "done")),
            "dropped" => entries.Where(entry => HasStatus(entry, "dropped")),
            _ => entries
        };

    private static string NormalizeSection(string? section) => section?.Trim().ToLowerInvariant() switch
    {
        "updates" or "planned" or "reading" or "paused" or "done" or "dropped" or "all" => section.Trim().ToLowerInvariant(),
        _ => "all"
    };

    private static bool HasStatus(MangaEntryResponse entry, string status) =>
        string.Equals(entry.ReadingStatus, status, StringComparison.OrdinalIgnoreCase);

    private static int DisplayRank(MangaEntryResponse entry)
    {
        if (IsReadingWithNewChapters(entry)) return 0;
        if (entry.IsManualReleaseCheckDue) return 1;

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

    private static bool NeedsManualReleaseCheck(MangaEntryResponse entry, DateTimeOffset dueBefore) =>
        IsActivelyTracked(entry)
        && string.IsNullOrWhiteSpace(entry.MangaDexId)
        && (entry.LastExternalReaderVerifiedAt is null || entry.LastExternalReaderVerifiedAt <= dueBefore);

    public async Task<List<ExternalReaderCheckInResponse>> ListPendingExternalReaderCheckInsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.UserMangaEntries.AsNoTracking()
            .Where(entry => entry.UserId == userId && entry.ExternalReaderCheckPendingAt != null)
            .Where(entry => entry.MangaEntry != null
                && entry.MangaEntry.MangaDexId == ""
                && entry.MangaEntry.FallbackReaderUrl != ""
                && (entry.ReadingStatus == "reading" || entry.ReadingStatus == "paused"))
            .OrderBy(entry => entry.ExternalReaderCheckPendingAt)
            .Select(entry => new ExternalReaderCheckInResponse(
                entry.MangaEntryId,
                entry.MangaEntry!.Title,
                entry.CurrentChapter,
                entry.MangaEntry.FallbackReaderUrl,
                entry.ExternalReaderCheckPendingAt!.Value))
            .ToListAsync(cancellationToken);

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
