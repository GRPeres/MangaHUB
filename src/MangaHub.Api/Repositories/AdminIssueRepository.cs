using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class AdminIssueRepository(MangaHubDbContext db)
{
    public Task<AdminIssue?> GetOpenAsync(string kind, string subjectType, Guid subjectId, CancellationToken cancellationToken) =>
        db.AdminIssues.Include(issue => issue.Reports).FirstOrDefaultAsync(issue => issue.Kind == kind && issue.SubjectType == subjectType && issue.SubjectId == subjectId && issue.Status == "open", cancellationToken);

    public Task<AdminIssue?> GetDetailsAsync(Guid issueId, CancellationToken cancellationToken) =>
        db.AdminIssues.Include(issue => issue.Reports).FirstOrDefaultAsync(issue => issue.Id == issueId, cancellationToken);

    public async Task<List<AdminIssueListItemResponse>> ListAsync(string? status, int offset, int limit, CancellationToken cancellationToken)
    {
        var query = db.AdminIssues.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(issue => issue.Status == status.Trim().ToLowerInvariant());
        }

        return await query.OrderBy(issue => issue.Status == "open" ? 0 : 1).ThenByDescending(issue => issue.UpdatedAt)
            .Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 100))
            .Select(issue => new AdminIssueListItemResponse(
                issue.Id, issue.Kind, issue.SubjectType, issue.SubjectId, issue.Status, issue.Priority,
                issue.TitleSnapshot,
                db.MangaEntries.Where(manga => manga.Id == issue.SubjectId).Select(manga => manga.CoverUrl).FirstOrDefault() ?? "",
                db.AdminIssueReports.Count(report => report.AdminIssueId == issue.Id), issue.CreatedAt, issue.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountOpenAsync(CancellationToken cancellationToken) =>
        db.AdminIssues.CountAsync(issue => issue.Status == "open", cancellationToken);

    public void Add(AdminIssue issue) => db.AdminIssues.Add(issue);
    public void AddReport(AdminIssueReport report) => db.AdminIssueReports.Add(report);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
