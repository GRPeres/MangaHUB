using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class CatalogRepository(MangaHubDbContext db)
{
    private static readonly string[] DuplicateIdentityProviders = ["myanimelist", "mangadex", "mangaupdates", "openlibrary"];
    public Task<MangaEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.MangaEntries.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<MangaEntry?> GetByIdNoTrackingAsync(Guid id, CancellationToken cancellationToken) =>
        db.MangaEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<MangaEntry?> FindByMangaDexIdAsync(string mangaDexId, CancellationToken cancellationToken) =>
        db.MangaEntries.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(x => x.MangaDexId == mangaDexId, cancellationToken);

    public Task<MangaEntry?> FindByMyAnimeListIdAsync(string myAnimeListId, CancellationToken cancellationToken) =>
        db.MangaEntries.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(x => x.MyAnimeListId == myAnimeListId, cancellationToken);

    public Task<MangaEntry?> FindByMangaUpdatesIdAsync(string mangaUpdatesId, CancellationToken cancellationToken) =>
        db.MangaEntries.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(x => x.MangaUpdatesId == mangaUpdatesId, cancellationToken);

    public Task<MangaEntry?> FindByOpenLibraryKeyAsync(string openLibraryKey, CancellationToken cancellationToken) =>
        db.MangaEntries.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(x => x.OpenLibraryKey == openLibraryKey, cancellationToken);

    public Task<MangaEntry?> FindByReaderUrlAsync(string url, CancellationToken cancellationToken) =>
        db.MangaEntries.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(x => x.FallbackReaderUrl == url, cancellationToken);

    public Task<MangaEntry?> FindByTitleAsync(string title, CancellationToken cancellationToken) =>
        db.MangaEntries.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(x => x.Title.ToLower() == title.Trim().ToLower(), cancellationToken);

    public Task<bool> IsInUserShelfAsync(Guid userId, Guid mangaEntryId, CancellationToken cancellationToken) =>
        db.UserMangaEntries.AnyAsync(x => x.UserId == userId && x.MangaEntryId == mangaEntryId, cancellationToken);

    public async Task<List<CatalogMangaResponse>> SearchAsync(Guid userId, string? queryText, string preferredLanguage, int offset, int limit, CancellationToken cancellationToken)
    {
        var languageCodes = LanguagePreferences.Parse(preferredLanguage).ToArray();
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
            .Skip(offset)
            .Take(limit)
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
                x.MangaDexId,
                x.MangaDexLatestChapter,
                x.MangaDexLastSyncedAt,
                x.MangaUpdatesId,
                x.MangaUpdatesLatestChapter,
                x.MangaUpdatesStatus,
                x.MangaUpdatesCompleted,
                x.MangaUpdatesLastSyncedAt,
                x.LocalSeriesId,
                db.Series
                    .Where(series => series.Source == "mangadex-cache" && series.ExternalId == x.MangaDexId)
                    .SelectMany(series => series.Chapters)
                    .Count(),
                shelfIds.Contains(x.Id),
                x.FallbackReaderUrl,
                x.ReaderPreference,
                db.MangaDexLanguageLatestChapters.Where(latest => latest.MangaEntryId == x.Id && languageCodes.Contains(latest.Language))
                    .Select(latest => (decimal?)latest.LatestChapter).Max()))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(MangaEntry manga, CancellationToken cancellationToken)
    {
        db.MangaEntries.Add(manga);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task<List<DuplicateCatalogIdentityGroup>> FindDuplicateIdentityGroupsAsync(CancellationToken cancellationToken)
    {
        var entries = await db.MangaEntries.AsNoTracking()
            .Select(manga => new DuplicateCatalogMangaResponse(manga.Id, manga.Title, manga.CoverUrl, manga.MyAnimeListId, manga.MangaDexId, manga.MangaUpdatesId, manga.OpenLibraryKey, manga.CreatedAt))
            .ToListAsync(cancellationToken);

        return DuplicateIdentityProviders
            .SelectMany(provider => entries
                .Select(entry => new { Entry = entry, Value = GetIdentityValue(entry, provider) })
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .GroupBy(item => item.Value.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(item => item.Entry.Id).Distinct().Count() > 1)
                .Select(group => new DuplicateCatalogIdentityGroup(
                    provider,
                    group.Key,
                    group.Select(item => item.Entry).OrderBy(item => item.CreatedAt).ToList())))
            .ToList();
    }

    public async Task<List<DuplicateCatalogMangaResponse>> GetDuplicateIdentityEntriesAsync(string provider, string value, CancellationToken cancellationToken)
    {
        if (!DuplicateIdentityProviders.Contains(provider, StringComparer.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(value)) return [];
        var normalized = value.Trim();
        var query = db.MangaEntries.AsNoTracking().AsQueryable();
        query = provider.ToLowerInvariant() switch
        {
            "myanimelist" => query.Where(manga => manga.MyAnimeListId == normalized),
            "mangadex" => query.Where(manga => manga.MangaDexId == normalized),
            "mangaupdates" => query.Where(manga => manga.MangaUpdatesId == normalized),
            "openlibrary" => query.Where(manga => manga.OpenLibraryKey == normalized),
            _ => query.Where(_ => false)
        };

        return await query.OrderBy(manga => manga.CreatedAt)
            .Select(manga => new DuplicateCatalogMangaResponse(manga.Id, manga.Title, manga.CoverUrl, manga.MyAnimeListId, manga.MangaDexId, manga.MangaUpdatesId, manga.OpenLibraryKey, manga.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> MergeDuplicateEntriesAsync(Guid keepMangaEntryId, Guid removeMangaEntryId, CancellationToken cancellationToken)
    {
        if (keepMangaEntryId == removeMangaEntryId) return false;
        var keep = await db.MangaEntries.FirstOrDefaultAsync(manga => manga.Id == keepMangaEntryId, cancellationToken);
        var remove = await db.MangaEntries.FirstOrDefaultAsync(manga => manga.Id == removeMangaEntryId, cancellationToken);
        if (keep is null || remove is null) return false;

        var keptShelfEntries = await db.UserMangaEntries.Where(entry => entry.MangaEntryId == keepMangaEntryId).ToDictionaryAsync(entry => entry.UserId, cancellationToken);
        var removedShelfEntries = await db.UserMangaEntries.Where(entry => entry.MangaEntryId == removeMangaEntryId).ToListAsync(cancellationToken);
        foreach (var removedShelf in removedShelfEntries)
        {
            if (keptShelfEntries.TryGetValue(removedShelf.UserId, out var keptShelf))
            {
                MergeShelfEntry(keptShelf, removedShelf);
                db.UserMangaEntries.Remove(removedShelf);
            }
            else
            {
                removedShelf.MangaEntryId = keepMangaEntryId;
            }
        }

        var keptLatest = await db.MangaDexLanguageLatestChapters.Where(latest => latest.MangaEntryId == keepMangaEntryId).ToDictionaryAsync(latest => latest.Language, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var removedLatest = await db.MangaDexLanguageLatestChapters.Where(latest => latest.MangaEntryId == removeMangaEntryId).ToListAsync(cancellationToken);
        foreach (var latest in removedLatest)
        {
            if (keptLatest.TryGetValue(latest.Language, out var existing))
            {
                if (latest.LatestChapter > existing.LatestChapter) existing.LatestChapter = latest.LatestChapter;
                if (latest.SyncedAt > existing.SyncedAt) existing.SyncedAt = latest.SyncedAt;
                db.MangaDexLanguageLatestChapters.Remove(latest);
            }
            else
            {
                latest.MangaEntryId = keepMangaEntryId;
            }
        }

        var notificationKeys = await db.Notifications.Where(notification => notification.MangaEntryId == keepMangaEntryId)
            .Select(notification => new { notification.UserId, notification.Type, notification.ChapterNumber, notification.Language })
            .ToListAsync(cancellationToken);
        var existingNotificationKeys = notificationKeys.Select(key => $"{key.UserId:N}|{key.Type}|{key.ChapterNumber}|{key.Language}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removedNotifications = await db.Notifications.Where(notification => notification.MangaEntryId == removeMangaEntryId).ToListAsync(cancellationToken);
        foreach (var notification in removedNotifications)
        {
            var key = $"{notification.UserId:N}|{notification.Type}|{notification.ChapterNumber}|{notification.Language}";
            if (!existingNotificationKeys.Add(key)) db.Notifications.Remove(notification);
            else notification.MangaEntryId = keepMangaEntryId;
        }

        var usageEvents = await db.UsageEvents.Where(entry => entry.MangaEntryId == removeMangaEntryId).ToListAsync(cancellationToken);
        foreach (var usageEvent in usageEvents) usageEvent.MangaEntryId = keepMangaEntryId;
        var linkedIssues = await db.AdminIssues.Where(issue => issue.SubjectType == AdminIssueTypes.CatalogManga && issue.SubjectId == removeMangaEntryId).ToListAsync(cancellationToken);
        foreach (var issue in linkedIssues) issue.SubjectId = keepMangaEntryId;

        db.MangaEntries.Remove(remove);
        keep.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string GetIdentityValue(DuplicateCatalogMangaResponse entry, string provider) => provider switch
    {
        "myanimelist" => entry.MyAnimeListId,
        "mangadex" => entry.MangaDexId,
        "mangaupdates" => entry.MangaUpdatesId,
        "openlibrary" => entry.OpenLibraryKey,
        _ => ""
    };

    private static void MergeShelfEntry(UserMangaEntry keep, UserMangaEntry remove)
    {
        var keepChapter = ParseChapterNumber(keep.CurrentChapter);
        var removeChapter = ParseChapterNumber(remove.CurrentChapter);
        var useRemovedProgress = removeChapter > keepChapter;
        if (useRemovedProgress)
        {
            keep.CurrentChapter = remove.CurrentChapter;
            keep.IsRead = remove.IsRead;
        }
        else if (removeChapter == keepChapter)
        {
            keep.IsRead |= remove.IsRead;
        }

        if (remove.UpdatedAt > keep.UpdatedAt)
        {
            keep.ReadingStatus = remove.ReadingStatus;
            keep.Score = remove.Score ?? keep.Score;
            keep.Category = FirstNonEmpty(remove.Category, keep.Category);
            keep.Summary = FirstNonEmpty(remove.Summary, keep.Summary);
            keep.LastExternalReaderOpenedAt = remove.LastExternalReaderOpenedAt ?? keep.LastExternalReaderOpenedAt;
            keep.LastExternalReaderVerifiedAt = remove.LastExternalReaderVerifiedAt ?? keep.LastExternalReaderVerifiedAt;
            keep.ExternalReaderCheckPendingAt = remove.ExternalReaderCheckPendingAt ?? keep.ExternalReaderCheckPendingAt;
        }

        keep.Notes = MergeNotes(keep.Notes, remove.Notes);
        keep.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static decimal ParseChapterNumber(string value)
    {
        var normalized = new string((value ?? "").Where(character => char.IsDigit(character) || character is '.' or ',').ToArray()).Replace(',', '.');
        return decimal.TryParse(normalized, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : -1m;
    }

    private static string FirstNonEmpty(string first, string second) => string.IsNullOrWhiteSpace(first) ? second : first;

    private static string MergeNotes(string first, string second) => string.IsNullOrWhiteSpace(second) || first.Contains(second, StringComparison.Ordinal)
        ? first
        : string.IsNullOrWhiteSpace(first) ? second : $"{first}\n\n{second}";
}

public sealed record DuplicateCatalogIdentityGroup(string Provider, string Value, List<DuplicateCatalogMangaResponse> Entries);
