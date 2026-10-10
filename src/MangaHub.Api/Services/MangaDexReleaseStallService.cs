using System.Text.Json;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Services;

/// <summary>
/// Detects a MangaDex title which has remained ongoing and unchanged for a full month.
/// A hiatus, completed, or otherwise non-ongoing status resets the clock.
/// </summary>
public sealed class MangaDexReleaseStallService(MangaHubDbContext db, ILogger<MangaDexReleaseStallService> logger)
{
    private static readonly TimeSpan StallWindow = TimeSpan.FromDays(30);

    public async Task<ReleaseStallCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - StallWindow;
        var stalled = await db.MangaEntries
            .Where(entry => entry.MangaDexId != ""
                && entry.PublishingStatus.ToLower() == "ongoing"
                && entry.MangaDexNonHiatusSince != null && entry.MangaDexNonHiatusSince <= cutoff
                && entry.MangaDexLatestChapterObservedAt != null && entry.MangaDexLatestChapterObservedAt <= cutoff)
            .ToListAsync(cancellationToken);
        var stalledIds = stalled.Select(entry => entry.Id).ToHashSet();
        var openIssues = await db.AdminIssues
            .Where(issue => issue.Kind == AdminIssueTypes.MangaDexReleaseStalled
                && issue.SubjectType == AdminIssueTypes.CatalogManga
                && issue.Status == "open")
            .ToListAsync(cancellationToken);

        var openedOrUpdated = 0;
        foreach (var manga in stalled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inactiveDays = Math.Floor((now - manga.MangaDexLatestChapterObservedAt!.Value).TotalDays);
            var metadata = JsonSerializer.Serialize(new
            {
                latestChapter = manga.MangaDexLatestChapter,
                latestChapterObservedAt = manga.MangaDexLatestChapterObservedAt,
                nonHiatusSince = manga.MangaDexNonHiatusSince,
                inactiveDays,
                checkedAt = now
            });
            var issue = openIssues.FirstOrDefault(item => item.SubjectId == manga.Id);
            if (issue is null)
            {
                db.AdminIssues.Add(new AdminIssue
                {
                    Kind = AdminIssueTypes.MangaDexReleaseStalled,
                    SubjectType = AdminIssueTypes.CatalogManga,
                    SubjectId = manga.Id,
                    Priority = inactiveDays >= 90 ? "high" : "normal",
                    TitleSnapshot = $"No MangaDex chapter for {inactiveDays:0} days: {manga.Title}",
                    MetadataJson = metadata,
                    ResolutionNote = "MangaDex has reported this series as ongoing, but no page-bearing chapter has appeared for at least 30 non-hiatus days. Check an external source or confirm its current status."
                });
            }
            else
            {
                issue.MetadataJson = metadata;
                issue.Priority = inactiveDays >= 90 ? "high" : "normal";
                issue.UpdatedAt = now;
            }
            openedOrUpdated++;
        }

        var resolved = 0;
        foreach (var issue in openIssues.Where(issue => !stalledIds.Contains(issue.SubjectId)))
        {
            issue.Status = "resolved";
            issue.ResolvedAt = now;
            issue.UpdatedAt = now;
            issue.ResolutionNote = "A MangaDex release arrived or the series is no longer continuously ongoing.";
            resolved++;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("MangaDex stalled-release check found {StalledCount} stalled manga, opened or refreshed {IssueCount} issues, and resolved {ResolvedCount} recovered issues.", stalled.Count, openedOrUpdated, resolved);
        return new ReleaseStallCheckResult(stalled.Count, openedOrUpdated, resolved);
    }
}

public sealed record ReleaseStallCheckResult(int StalledManga, int OpenedOrUpdatedIssues, int ResolvedIssues);
