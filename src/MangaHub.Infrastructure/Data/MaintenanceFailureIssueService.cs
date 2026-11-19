using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Infrastructure.Data;

/// <summary>
/// Keeps a single actionable issue for each maintenance task that is currently failing.
/// The worker owns the job state; this service only updates the corresponding issue.
/// </summary>
public sealed class MaintenanceFailureIssueService(MangaHubDbContext db)
{
    public async Task RecordFailureAsync(MaintenanceJob job, CancellationToken cancellationToken)
    {
        var subjectId = GetSubjectId(job.Type);
        var related = db.AdminIssues.Where(item =>
            item.Kind == AdminIssueTypes.MaintenanceFailure
            && item.SubjectType == AdminIssueTypes.MaintenanceTask
            && item.SubjectId == subjectId);
        var issue = await related.FirstOrDefaultAsync(item => item.Status == "open", cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var error = Limit(job.Error.Trim(), 2000);
        var metadata = JsonSerializer.Serialize(new
        {
            job.Type,
            job.Trigger,
            job.Id,
            job.RequestedAt,
            job.StartedAt,
            job.CompletedAt,
            error
        });

        if (issue is null)
        {
            var previous = await related.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (previous is { Status: "dismissed", ResolvedAt: { } dismissedAt }
                && HasSameError(previous.MetadataJson, error)
                && !await db.MaintenanceJobs.AnyAsync(run => run.Type == job.Type && run.Status == "completed"
                    && run.CompletedAt > dismissedAt && run.CompletedAt <= (job.CompletedAt ?? now), cancellationToken))
                return;

            db.AdminIssues.Add(new AdminIssue
            {
                Kind = AdminIssueTypes.MaintenanceFailure,
                SubjectType = AdminIssueTypes.MaintenanceTask,
                SubjectId = subjectId,
                Priority = GetPriority(job.Type),
                TitleSnapshot = $"Maintenance task failed: {job.Type}",
                MetadataJson = metadata,
                LastCheckedAt = now,
                ResolutionNote = "Review the latest error, then retry this task from Operations."
            });
            return;
        }

        issue.Priority = GetPriority(job.Type);
        issue.MetadataJson = metadata;
        issue.LastCheckedAt = now;
        issue.UpdatedAt = now;
        issue.ResolutionNote = "The task failed again. Review the latest error, then retry it from Operations.";
    }

    public async Task ResolveAfterSuccessAsync(MaintenanceJob job, CancellationToken cancellationToken)
    {
        var issue = await db.AdminIssues.FirstOrDefaultAsync(item =>
            item.Kind == AdminIssueTypes.MaintenanceFailure
            && item.SubjectType == AdminIssueTypes.MaintenanceTask
            && item.SubjectId == GetSubjectId(job.Type)
            && item.Status == "open", cancellationToken);
        if (issue is null)
        {
            return;
        }

        issue.Status = "resolved";
        issue.ReviewState = "auto_resolved";
        issue.SnoozedUntil = null;
        issue.LastCheckedAt = DateTimeOffset.UtcNow;
        issue.ResolvedAt = DateTimeOffset.UtcNow;
        issue.UpdatedAt = DateTimeOffset.UtcNow;
        issue.ResolutionNote = $"A later {job.Trigger} run completed successfully.";
    }

    private static bool HasSameError(string metadata, string error)
    {
        try
        {
            using var document = JsonDocument.Parse(metadata);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var value)
                && value.ValueKind == JsonValueKind.String && value.GetString()?.Trim() == error;
        }
        catch (JsonException) { return false; }
    }

    private static string GetPriority(string type) => type is "release-sync" or "mangadex-status-sync" or "mangaupdates-sync"
        ? "high"
        : "normal";

    private static Guid GetSubjectId(string type) => new(MD5.HashData(Encoding.UTF8.GetBytes($"mangahub-maintenance-failure:{type.Trim().ToLowerInvariant()}")));
    private static string Limit(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
