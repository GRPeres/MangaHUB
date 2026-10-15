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
        var issue = await db.AdminIssues.FirstOrDefaultAsync(item =>
            item.Kind == AdminIssueTypes.MaintenanceFailure
            && item.SubjectType == AdminIssueTypes.MaintenanceTask
            && item.SubjectId == subjectId
            && item.Status == "open", cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var metadata = JsonSerializer.Serialize(new
        {
            job.Type,
            job.Trigger,
            job.Id,
            job.RequestedAt,
            job.StartedAt,
            job.CompletedAt,
            error = Limit(job.Error, 2000)
        });

        if (issue is null)
        {
            db.AdminIssues.Add(new AdminIssue
            {
                Kind = AdminIssueTypes.MaintenanceFailure,
                SubjectType = AdminIssueTypes.MaintenanceTask,
                SubjectId = subjectId,
                Priority = GetPriority(job.Type),
                TitleSnapshot = $"Maintenance task failed: {job.Type}",
                MetadataJson = metadata,
                ResolutionNote = "Review the latest error, then retry this task from Operations."
            });
            return;
        }

        issue.Priority = GetPriority(job.Type);
        issue.MetadataJson = metadata;
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
        issue.ResolvedAt = DateTimeOffset.UtcNow;
        issue.UpdatedAt = DateTimeOffset.UtcNow;
        issue.ResolutionNote = $"A later {job.Trigger} run completed successfully.";
    }

    private static string GetPriority(string type) => type is "release-sync" or "mangadex-status-sync" or "mangaupdates-sync"
        ? "high"
        : "normal";

    private static Guid GetSubjectId(string type) => new(MD5.HashData(Encoding.UTF8.GetBytes($"mangahub-maintenance-failure:{type.Trim().ToLowerInvariant()}")));
    private static string Limit(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
