using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class AdminIssueRepository(MangaHubDbContext db)
{
    public Task<AdminIssue?> GetOpenAsync(string kind, string subjectType, Guid subjectId, CancellationToken cancellationToken) =>
        db.AdminIssues.Include(issue => issue.Reports).FirstOrDefaultAsync(issue => issue.Kind == kind && issue.SubjectType == subjectType && issue.SubjectId == subjectId && issue.Status == "open", cancellationToken);

    public Task<List<AdminIssue>> GetOpenByKindsAsync(IEnumerable<string> kinds, string subjectType, Guid subjectId, CancellationToken cancellationToken)
    {
        var values = kinds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return db.AdminIssues.Include(issue => issue.Reports)
            .Where(issue => values.Contains(issue.Kind) && issue.SubjectType == subjectType && issue.SubjectId == subjectId && issue.Status == "open")
            .ToListAsync(cancellationToken);
    }

    public Task<AdminIssue?> GetDetailsAsync(Guid issueId, CancellationToken cancellationToken) =>
        db.AdminIssues.Include(issue => issue.Reports).FirstOrDefaultAsync(issue => issue.Id == issueId, cancellationToken);

    public async Task<List<AdminIssueListItemResponse>> ListAsync(string? status, int offset, int limit, CancellationToken cancellationToken)
    {
        var query = db.AdminIssues.AsNoTracking();
        var now = DateTimeOffset.UtcNow;
        if (status == "action" || status == "review")
        {
            query = query.Where(issue => issue.Status == "open");
            query = status == "action" ? Actionable(query, now) : query.Where(issue =>
                issue.ReviewState != "action_required" || issue.SnoozedUntil > now);
        }
        else if (status == "history")
            query = query.Where(issue => issue.Status != "open");
        else if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(issue => issue.Status == status.Trim().ToLowerInvariant());
        }

        var page = await query.OrderBy(issue => issue.Status == "open" ? 0 : 1).ThenByDescending(issue => issue.UpdatedAt)
            .ThenBy(issue => issue.Id).Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 100))
            .Select(issue => new { issue.MetadataJson, Item = new AdminIssueListItemResponse(
                issue.Id, issue.Kind, issue.SubjectType, issue.SubjectId, issue.Status, issue.Priority,
                issue.TitleSnapshot,
                db.MangaEntries.Where(manga => manga.Id == issue.SubjectId).Select(manga => manga.CoverUrl).FirstOrDefault() ?? "",
                db.AdminIssueReports.Count(report => report.AdminIssueId == issue.Id), issue.CreatedAt, issue.UpdatedAt,
                issue.ReviewState, issue.SnoozedUntil, issue.LastCheckedAt, "") })
            .ToListAsync(cancellationToken);
        return page.Select(row => row.Item with
        {
            Diagnosis = DescribeIssue(row.Item.Kind, row.MetadataJson)
        }).ToList();
    }

    private static string DescribeIssue(string kind, string metadata)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(metadata);
            var root = document.RootElement;
            string? Value(string key) => root.ValueKind == System.Text.Json.JsonValueKind.Object
                && root.TryGetProperty(key, out var node)
                && node.ValueKind is System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.String
                ? node.ToString() : null;

            return kind switch
            {
                "mangadex-release-stalled" when Value("referenceChapter") is { } reference
                    && Value("latestChapter") is { } latest =>
                    $"MangaDex has chapter {latest}; MangaUpdates lists chapter {reference} as a publication reference.",
                "mangadex-release-stalled" => "No recent MangaDex release. A newer readable chapter has not been confirmed.",
                "mangadex-identity-mismatch" when Value("expectedMyAnimeListId") is { } expected
                    && Value("linkedMyAnimeListId") is { } linked =>
                    $"Catalog expects MAL #{expected}, but the linked MangaDex title reports MAL #{linked}.",
                "maintenance-failure" when Value("error") is { } error =>
                    error.Length > 160 ? error[..160].TrimEnd() + "..." : error,
                _ => ""
            };
        }
        catch (System.Text.Json.JsonException)
        {
            return "";
        }
    }

    public Task<int> CountOpenAsync(CancellationToken cancellationToken) =>
        Actionable(db.AdminIssues.Where(issue => issue.Status == "open"), DateTimeOffset.UtcNow).CountAsync(cancellationToken);

    public async Task<IssueHealthResponse> GetHealthAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-7);
        var open = db.AdminIssues.AsNoTracking().Where(issue => issue.Status == "open");
        var actionable = await Actionable(open, now).CountAsync(cancellationToken);
        var inReview = await open.CountAsync(issue => issue.ReviewState != "action_required" || issue.SnoozedUntil > now, cancellationToken);
        var autoResolved = await db.AdminIssues.CountAsync(issue => issue.Status == "resolved"
            && issue.ReviewState == "auto_resolved" && issue.ResolvedAt >= since && issue.ResolvedAt <= now, cancellationToken);
        var oldest = await open.OrderBy(issue => issue.CreatedAt).Select(issue => (DateTimeOffset?)issue.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return new(actionable, inReview, autoResolved, oldest);
    }

    private static IQueryable<AdminIssue> Actionable(IQueryable<AdminIssue> query, DateTimeOffset now) =>
        query.Where(issue => issue.ReviewState == "action_required"
            && (issue.SnoozedUntil == null || issue.SnoozedUntil <= now));

    public void Add(AdminIssue issue) => db.AdminIssues.Add(issue);
    public void AddReport(AdminIssueReport report) => db.AdminIssueReports.Add(report);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
