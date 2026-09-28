using MangaHub.Core.Dto;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class DashboardRepository(MangaHubDbContext db)
{
    public async Task<HomeDashboardResponse> GetAsync(Guid userId, string preferredLanguage, CancellationToken cancellationToken)
    {
        var languageCodes = LanguagePreferences.Parse(preferredLanguage).ToArray();
        var shelfEntries = await db.UserMangaEntries.AsNoTracking()
            .Where(entry => entry.UserId == userId)
            .Select(entry => new HomeDashboardMangaResponse(
                entry.MangaEntryId,
                entry.MangaEntry!.Title,
                entry.MangaEntry.CoverUrl,
                entry.ReadingStatus,
                entry.CurrentChapter,
                entry.Score,
                entry.Category,
                entry.Summary,
                entry.Notes,
                entry.MangaEntry.MediaType,
                entry.MangaEntry.FirstPublishYear,
                db.MangaDexLanguageLatestChapters
                    .Where(latest => latest.MangaEntryId == entry.MangaEntryId && languageCodes.Contains(latest.Language))
                    .Select(latest => (decimal?)latest.LatestChapter)
                    .Max(),
                entry.IsRead))
            .ToListAsync(cancellationToken);

        var newReleases = shelfEntries
            .Where(IsReadingWithNewChapters)
            .OrderByDescending(ReleaseGap)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        var planned = shelfEntries
            .Where(entry => HasStatus(entry, "planned"))
            .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var continueReading = shelfEntries
            .Where(entry => HasStatus(entry, "reading"))
            .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? planned.FirstOrDefault();
        var pendingRatings = shelfEntries
            .Where(entry => HasStatus(entry, "done") && entry.Score is null)
            .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();

        var shelfIds = db.UserMangaEntries.Where(entry => entry.UserId == userId).Select(entry => entry.MangaEntryId);
        var recommendations = await db.MangaEntries.AsNoTracking()
            .Where(entry => !shelfIds.Contains(entry.Id))
            .OrderBy(entry => entry.Title)
            .Take(3)
            .Select(entry => new HomeDashboardMangaResponse(
                entry.Id,
                entry.Title,
                entry.CoverUrl,
                "",
                "",
                null,
                "",
                "",
                "",
                entry.MediaType,
                entry.FirstPublishYear,
                null,
                false))
            .ToListAsync(cancellationToken);

        return new HomeDashboardResponse(
            continueReading,
            shelfEntries.Count(IsReadingWithNewChapters),
            newReleases,
            planned.Count,
            recommendations,
            pendingRatings);
    }

    private static bool HasStatus(HomeDashboardMangaResponse entry, string status) =>
        string.Equals(entry.ReadingStatus, status, StringComparison.OrdinalIgnoreCase);

    private static bool IsReadingWithNewChapters(HomeDashboardMangaResponse entry) =>
        (HasStatus(entry, "reading") || HasStatus(entry, "paused"))
        && entry.MangaDexPreferredLanguageLatestChapter is { } latest
        && (latest > ParseChapter(entry.CurrentChapter)
            || (latest == ParseChapter(entry.CurrentChapter) && !entry.IsRead));

    private static decimal ReleaseGap(HomeDashboardMangaResponse entry)
    {
        var gap = Math.Max(0, (entry.MangaDexPreferredLanguageLatestChapter ?? 0) - ParseChapter(entry.CurrentChapter));
        return gap == 0 && !entry.IsRead && entry.MangaDexPreferredLanguageLatestChapter == ParseChapter(entry.CurrentChapter) ? 1 : gap;
    }

    private static decimal ParseChapter(string value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
}
