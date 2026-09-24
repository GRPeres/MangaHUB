using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Services;

/// <summary>
/// Detects maintenance schedules that are overdue, retries each one once, and raises an admin issue only when recovery fails.
/// </summary>
public sealed class MaintenanceWatchdogService(
    MangaHubDbContext db,
    AdminOperationsService operations,
    IOptions<MangaHubOptions> options,
    ILogger<MaintenanceWatchdogService> logger)
{
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var task in GetExpectedTasks())
        {
            var latestSuccess = await db.MaintenanceJobs.AsNoTracking()
                .Where(job => job.Type == task.Type && job.Status == "completed" && job.CompletedAt != null)
                .OrderByDescending(job => job.CompletedAt)
                .Select(job => job.CompletedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var issueSubjectId = GetTaskSubjectId(task.Type);
            if (latestSuccess is not null && latestSuccess.Value >= now - task.Interval)
            {
                await ResolveRecoveredIssueAsync(issueSubjectId, cancellationToken);
                continue;
            }

            var activeRetry = await db.MaintenanceJobs.AsNoTracking()
                .AnyAsync(job => job.Type == task.Type && (job.Status == "queued" || job.Status == "running"), cancellationToken);
            if (activeRetry)
            {
                continue;
            }

            var latestWatchdogRetry = await db.MaintenanceJobs.AsNoTracking()
                .Where(job => job.Type == task.Type && job.Trigger == "watchdog")
                .OrderByDescending(job => job.RequestedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (latestWatchdogRetry is null || latestWatchdogRetry.RequestedAt < now - task.Interval)
            {
                await operations.QueueAutomaticAsync(task.Type, "watchdog", cancellationToken);
                logger.LogWarning("Maintenance task {Type} is overdue; queued an automatic watchdog retry.", task.Type);
                continue;
            }

            if (latestWatchdogRetry.Status == "failed")
            {
                await OpenOverdueIssueAsync(task, latestSuccess, latestWatchdogRetry.Error, cancellationToken);
            }
        }
    }

    private IEnumerable<ExpectedTask> GetExpectedTasks()
    {
        var value = options.Value;
        if (value.MangaDexEnabled)
        {
            yield return new("release-sync", TimeSpan.FromMinutes(Math.Clamp(value.MangaDexReleasePollMinutes, 15, 720)));
            yield return new("prefetch", TimeSpan.FromHours(24));
            if (value.MangaDexCacheRetentionEnabled) yield return new("mangadex-cache-cleanup", TimeSpan.FromHours(24));
            if (value.MangaDexIdleBackfillEnabled) yield return new("idle-backfill", TimeSpan.FromMinutes(Math.Clamp(value.MangaDexIdleBackfillCheckMinutes, 5, 720)));
        }

        yield return new(CatalogIdentityEnrichmentService.JobType, TimeSpan.FromMinutes(Math.Clamp(value.MangaUpdatesMatchPollMinutes, 5, 720)));
        if (value.MangaUpdatesEnabled) yield return new("mangaupdates-sync", TimeSpan.FromMinutes(Math.Clamp(value.MangaUpdatesReleasePollMinutes, 15, 720)));
        yield return new("library-scan", TimeSpan.FromHours(1));
    }

    private async Task OpenOverdueIssueAsync(ExpectedTask task, DateTimeOffset? latestSuccess, string error, CancellationToken cancellationToken)
    {
        var subjectId = GetTaskSubjectId(task.Type);
        var existing = await db.AdminIssues.FirstOrDefaultAsync(issue => issue.Kind == AdminIssueTypes.MaintenanceOverdue
            && issue.SubjectType == AdminIssueTypes.MaintenanceTask
            && issue.SubjectId == subjectId
            && issue.Status == "open", cancellationToken);
        if (existing is not null) return;

        db.AdminIssues.Add(new AdminIssue
        {
            Kind = AdminIssueTypes.MaintenanceOverdue,
            SubjectType = AdminIssueTypes.MaintenanceTask,
            SubjectId = subjectId,
            Priority = "high",
            TitleSnapshot = $"Maintenance overdue: {task.Type}",
            MetadataJson = JsonSerializer.Serialize(new { task.Type, ExpectedIntervalMinutes = (int)task.Interval.TotalMinutes, LastSuccessfulAt = latestSuccess, RetryError = error }),
            ResolutionNote = "The watchdog retry did not complete. Run this task manually from Operations."
        });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogError("Maintenance task {Type} remains overdue after a watchdog retry; opened an admin issue.", task.Type);
    }

    private async Task ResolveRecoveredIssueAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        var issue = await db.AdminIssues.FirstOrDefaultAsync(item => item.Kind == AdminIssueTypes.MaintenanceOverdue
            && item.SubjectType == AdminIssueTypes.MaintenanceTask
            && item.SubjectId == subjectId
            && item.Status == "open", cancellationToken);
        if (issue is null) return;

        issue.Status = "resolved";
        issue.ResolvedAt = DateTimeOffset.UtcNow;
        issue.UpdatedAt = DateTimeOffset.UtcNow;
        issue.ResolutionNote = "The task completed successfully after the watchdog alert.";
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Guid GetTaskSubjectId(string type) => new(MD5.HashData(Encoding.UTF8.GetBytes($"mangahub-maintenance:{type}")));

    private sealed record ExpectedTask(string Type, TimeSpan Interval);
}
