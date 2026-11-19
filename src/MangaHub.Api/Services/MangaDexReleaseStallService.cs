using System.Text.Json;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Services;

/// <summary>
/// Detects a MangaDex title whose newest readable chapter was published over a month ago.
/// A hiatus, completed, or otherwise non-ongoing status resets the clock.
/// </summary>
public sealed class MangaDexReleaseStallService(MangaHubDbContext db, ILogger<MangaDexReleaseStallService> logger)
{
    private static readonly TimeSpan StallWindow = TimeSpan.FromDays(30);

    public Task<ReleaseStallCheckResult> RunAsync(CancellationToken cancellationToken) => RunAsync(null, cancellationToken);

    public async Task<ReleaseStallCheckResult> RunAsync(Guid? subjectId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - StallWindow;
        var stalled = await db.MangaEntries
            .Where(entry => (subjectId == null || entry.Id == subjectId) && entry.MangaDexId != ""
                && entry.PublishingStatus.ToLower() == "ongoing"
                && entry.MangaDexNonHiatusSince != null && entry.MangaDexNonHiatusSince <= cutoff
                && (entry.MangaDexLatestChapterPublishedAt ?? entry.MangaDexLatestChapterObservedAt) != null
                && (entry.MangaDexLatestChapterPublishedAt ?? entry.MangaDexLatestChapterObservedAt) <= cutoff)
            .ToListAsync(cancellationToken);
        stalled.RemoveAll(entry => entry.MangaDexReleaseIntervalSamples >= 3
            && entry.MangaDexReleaseIntervalDays is { } interval
            && (entry.MangaDexLatestChapterPublishedAt ?? entry.MangaDexLatestChapterObservedAt)
                > now.AddDays(-Math.Clamp(interval * 1.5, 30, 180)));
        var stalledIds = stalled.Select(entry => entry.Id).ToHashSet();
        var dismissed = (await db.AdminIssues.AsNoTracking()
            .Where(issue => issue.Kind == AdminIssueTypes.MangaDexReleaseStalled
                && issue.SubjectType == AdminIssueTypes.CatalogManga && issue.Status == "dismissed")
            .ToListAsync(cancellationToken))
            .ToLookup(issue => issue.SubjectId);
        var openIssues = await db.AdminIssues
            .Where(issue => issue.Kind == AdminIssueTypes.MangaDexReleaseStalled
                && issue.SubjectType == AdminIssueTypes.CatalogManga
                && (subjectId == null || issue.SubjectId == subjectId)
                && issue.Status == "open")
            .ToListAsync(cancellationToken);

        var openedOrUpdated = 0;
        foreach (var manga in stalled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latestChapterAt = manga.MangaDexLatestChapterPublishedAt ?? manga.MangaDexLatestChapterObservedAt!.Value;
            var inactivityStartedAt = latestChapterAt > manga.MangaDexNonHiatusSince!.Value
                ? latestChapterAt
                : manga.MangaDexNonHiatusSince.Value;
            var inactiveDays = Math.Floor((now - inactivityStartedAt).TotalDays);
            var externalCoversLatest = !string.IsNullOrWhiteSpace(manga.FallbackReaderUrl)
                && manga.ExternalReaderLatestChapter.HasValue
                && manga.MangaDexLatestChapter.HasValue
                && manga.ExternalReaderLatestChapter.Value >= manga.MangaDexLatestChapter.Value;
            var reference = !string.IsNullOrWhiteSpace(manga.MangaUpdatesId)
                && manga.MangaUpdatesLastSyncedAt >= now.AddDays(-7) ? manga.MangaUpdatesLatestChapter : null;
            var readable = externalCoversLatest ? manga.ExternalReaderLatestChapter : manga.MangaDexLatestChapter;
            var gap = reference - readable;
            var issue = openIssues.FirstOrDefault(item => item.SubjectId == manga.Id);
            var (previousSync, confirmations) = ReadConfirmation(issue?.MetadataJson);
            if (reference is not null)
            {
                if (gap is not >= 3)
                    confirmations = 0;
                else if (confirmations == 0)
                    confirmations = 1;
                else if (manga.MangaUpdatesLastSyncedAt > previousSync)
                    confirmations = 2;
                previousSync = manga.MangaUpdatesLastSyncedAt;
            }
            var needsAction = gap >= 3 && confirmations >= 2;
            var priority = needsAction ? (gap >= 10 ? "high" : "normal")
                : externalCoversLatest ? "low" : inactiveDays >= 90 ? "high" : "normal";
            var reviewState = needsAction ? "action_required"
                : externalCoversLatest || (manga.MangaUpdatesId != "" && reference is null) ? "waiting" : "monitoring";
            var metadata = JsonSerializer.Serialize(new
            {
                latestChapter = manga.MangaDexLatestChapter,
                latestChapterPublishedAt = manga.MangaDexLatestChapterPublishedAt,
                latestChapterObservedAt = manga.MangaDexLatestChapterObservedAt,
                nonHiatusSince = manga.MangaDexNonHiatusSince,
                inactiveDays,
                externalReaderLatestChapter = manga.ExternalReaderLatestChapter,
                externalReaderLatestChapterObservedAt = manga.ExternalReaderLatestChapterObservedAt,
                externalCoversLatest,
                referenceChapter = reference,
                referenceGap = gap,
                referenceSync = previousSync,
                confirmations,
                checkedAt = now
            });
            if (issue is null)
            {
                if (dismissed[manga.Id].Any(item => item.ResolvedAt >= inactivityStartedAt
                    && item.ResolvedAt >= manga.ExternalReaderLatestChapterObservedAt.GetValueOrDefault()
                    && (gap is not >= 3 || HasSameGap(item.MetadataJson, reference, gap))))
                    continue;
                db.AdminIssues.Add(new AdminIssue
                {
                    Kind = AdminIssueTypes.MangaDexReleaseStalled,
                    SubjectType = AdminIssueTypes.CatalogManga,
                    SubjectId = manga.Id,
                    Priority = priority,
                    ReviewState = reviewState,
                    LastCheckedAt = now,
                    TitleSnapshot = $"No MangaDex chapter for {inactiveDays:0} days: {manga.Title}",
                    MetadataJson = metadata,
                    ResolutionNote = "MangaDex has reported this series as ongoing, but no page-bearing chapter has appeared for at least 30 non-hiatus days. Check an external source or confirm its current status."
                });
            }
            else
            {
                issue.MetadataJson = metadata;
                issue.Priority = priority;
                issue.ReviewState = reviewState;
                issue.LastCheckedAt = now;
                issue.TitleSnapshot = $"No MangaDex chapter for {inactiveDays:0} days: {manga.Title}";
                issue.UpdatedAt = now;
            }
            openedOrUpdated++;
        }

        var resolved = 0;
        foreach (var issue in openIssues.Where(issue => !stalledIds.Contains(issue.SubjectId)))
        {
            issue.Status = "resolved";
            issue.ReviewState = "auto_resolved";
            issue.LastCheckedAt = now;
            issue.ResolvedAt = now;
            issue.UpdatedAt = now;
            issue.ResolutionNote = "The series no longer exceeds its expected release window or is no longer continuously ongoing.";
            resolved++;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("MangaDex stalled-release check found {StalledCount} stalled manga, opened or refreshed {IssueCount} issues, and resolved {ResolvedCount} recovered issues.", stalled.Count, openedOrUpdated, resolved);
        return new ReleaseStallCheckResult(stalled.Count, openedOrUpdated, resolved);
    }

    private static (DateTimeOffset? Sync, int Count) ReadConfirmation(string? metadata)
    {
        if (metadata is null) return (null, 0);
        using var document = JsonDocument.Parse(metadata);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("referenceSync", out var sync) || sync.ValueKind != JsonValueKind.String
            || !sync.TryGetDateTimeOffset(out var date)
            || !root.TryGetProperty("confirmations", out var count) || count.ValueKind != JsonValueKind.Number
            || !count.TryGetInt32(out var value)) return (null, 0);
        return (date, Math.Clamp(value, 0, 2));
    }

    private static bool HasSameGap(string metadata, decimal? reference, decimal? gap)
    {
        using var document = JsonDocument.Parse(metadata);
        var root = document.RootElement;
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("referenceChapter", out var chapter) && chapter.ValueKind == JsonValueKind.Number
            && chapter.TryGetDecimal(out var chapterNumber) && chapterNumber == reference
            && root.TryGetProperty("referenceGap", out var difference) && difference.ValueKind == JsonValueKind.Number
            && difference.TryGetDecimal(out var amount) && amount == gap;
    }
}

public sealed record ReleaseStallCheckResult(int StalledManga, int OpenedOrUpdatedIssues, int ResolvedIssues);
