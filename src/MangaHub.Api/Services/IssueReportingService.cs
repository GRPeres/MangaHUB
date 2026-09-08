using System.Text.Json;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;

namespace MangaHub.Api.Services;

public sealed class IssueReportingService(
    AdminIssueRepository issues,
    ShelfRepository shelf,
    CatalogRepository catalog,
    NotificationRepository notifications,
    MangaDexCatalogMatchService mangaDexMatches)
{
    private static readonly HashSet<string> ExternalLinkReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "broken", "wrong-manga", "obsolete", "unsafe", "other"
    };

    public async Task<AdminIssueReportStateResponse?> ReportAsync(Guid userId, CreateAdminIssueReportRequest request, CancellationToken cancellationToken)
    {
        if (!AdminIssueTypes.Supports(request.Kind, request.SubjectType)
            || !string.Equals(request.Kind, AdminIssueTypes.ExternalReaderLink, StringComparison.OrdinalIgnoreCase)
            || !ExternalLinkReasons.Contains(request.Reason.Trim()))
        {
            return null;
        }

        var shelfEntry = await shelf.GetWithMangaAsync(userId, request.SubjectId, cancellationToken);
        if (shelfEntry?.MangaEntry is null
            || !string.IsNullOrWhiteSpace(shelfEntry.MangaEntry.MangaDexId)
            || !IsHttpUrl(shelfEntry.MangaEntry.FallbackReaderUrl))
        {
            return null;
        }

        var kind = AdminIssueTypes.ExternalReaderLink;
        var subjectType = AdminIssueTypes.CatalogManga;
        var issue = await issues.GetOpenAsync(kind, subjectType, request.SubjectId, cancellationToken);
        if (issue is null)
        {
            issue = new AdminIssue
            {
                Kind = kind,
                SubjectType = subjectType,
                SubjectId = request.SubjectId,
                Priority = "normal",
                TitleSnapshot = shelfEntry.MangaEntry.Title,
                MetadataJson = JsonSerializer.Serialize(new { fallbackReaderUrl = shelfEntry.MangaEntry.FallbackReaderUrl })
            };
            issues.Add(issue);
        }

        var alreadyReported = issue.Reports.Any(report => report.ReporterUserId == userId);
        var reportCount = issue.Reports.Count;
        if (!alreadyReported)
        {
            var report = new AdminIssueReport
            {
                AdminIssueId = issue.Id,
                ReporterUserId = userId,
                Reason = request.Reason.Trim().ToLowerInvariant(),
                Note = request.Note.Trim()[..Math.Min(request.Note.Trim().Length, 800)],
                SnapshotValue = shelfEntry.MangaEntry.FallbackReaderUrl
            };
            issues.AddReport(report);
            reportCount++;
        }

        issue.UpdatedAt = DateTimeOffset.UtcNow;
        await issues.SaveChangesAsync(cancellationToken);
        return new AdminIssueReportStateResponse(issue.Id, true, reportCount, issue.Status);
    }

    public async Task<AdminIssueReportStateResponse?> GetMyReportStateAsync(Guid userId, string kind, string subjectType, Guid subjectId, CancellationToken cancellationToken)
    {
        if (!AdminIssueTypes.Supports(kind, subjectType)) return null;
        var issue = await issues.GetOpenAsync(kind.Trim(), subjectType.Trim(), subjectId, cancellationToken);
        return issue is null
            ? new AdminIssueReportStateResponse(Guid.Empty, false, 0, "none")
            : new AdminIssueReportStateResponse(issue.Id, issue.Reports.Any(report => report.ReporterUserId == userId), issue.Reports.Count, issue.Status);
    }

    public Task<List<AdminIssueListItemResponse>> ListAsync(string? status, int offset, int limit, CancellationToken cancellationToken) =>
        issues.ListAsync(status, offset, limit, cancellationToken);

    public Task<int> CountOpenAsync(CancellationToken cancellationToken) => issues.CountOpenAsync(cancellationToken);

    public async Task<AdminIssueDetailsResponse?> GetDetailsAsync(Guid issueId, CancellationToken cancellationToken)
    {
        var issue = await issues.GetDetailsAsync(issueId, cancellationToken);
        if (issue is null) return null;
        var manga = issue.SubjectType == AdminIssueTypes.CatalogManga
            ? await catalog.GetByIdNoTrackingAsync(issue.SubjectId, cancellationToken)
            : null;
        return new AdminIssueDetailsResponse(issue.Id, issue.Kind, issue.SubjectType, issue.SubjectId, issue.Status, issue.Priority,
            issue.TitleSnapshot, manga?.CoverUrl ?? "", issue.MetadataJson, manga?.FallbackReaderUrl ?? "", manga?.MyAnimeListId ?? "", manga?.MangaDexId ?? "", manga?.MangaUpdatesId ?? "",
            issue.ResolutionNote, issue.CreatedAt, issue.UpdatedAt,
            issue.Reports.OrderByDescending(report => report.CreatedAt).Select(report => new AdminIssueReportResponse(report.Id, report.Reason, report.Note, report.SnapshotValue, report.CreatedAt)).ToList());
    }

    public async Task<bool> ResolveExternalReaderLinkAsync(Guid adminUserId, Guid issueId, ResolveAdminIssueRequest request, CancellationToken cancellationToken)
    {
        var issue = await issues.GetDetailsAsync(issueId, cancellationToken);
        if (issue is null || issue.Status != "open" || !string.Equals(issue.Kind, AdminIssueTypes.ExternalReaderLink, StringComparison.OrdinalIgnoreCase) || !IsHttpUrl(request.FallbackReaderUrl)) return false;
        var manga = await catalog.GetByIdAsync(issue.SubjectId, cancellationToken);
        if (manga is null) return false;

        manga.FallbackReaderUrl = request.FallbackReaderUrl.Trim();
        manga.UpdatedAt = DateTimeOffset.UtcNow;
        await CloseAsync(issue, adminUserId, "resolved", request.ResolutionNote, "External reader link repaired", "An admin repaired the external reader link for", cancellationToken);
        return true;
    }

    public async Task<MangaDexCatalogMatch?> ReintegrateWithMangaDexAsync(Guid adminUserId, Guid issueId, CancellationToken cancellationToken)
    {
        var issue = await issues.GetDetailsAsync(issueId, cancellationToken);
        if (issue is null
            || issue.Status != "open"
            || !string.Equals(issue.Kind, AdminIssueTypes.ExternalReaderLink, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var manga = await catalog.GetByIdAsync(issue.SubjectId, cancellationToken);
        if (manga is null || string.IsNullOrWhiteSpace(manga.MyAnimeListId))
        {
            return null;
        }

        var match = await mangaDexMatches.FindAsync(manga.MyAnimeListId, manga.Title, cancellationToken);
        if (match is null)
        {
            return null;
        }

        manga.MangaDexId = match.Id;
        manga.FallbackReaderUrl = "";
        manga.ReaderPreference = ReaderPreference.MangaHub;
        manga.UpdatedAt = DateTimeOffset.UtcNow;
        await CloseAsync(issue, adminUserId, "resolved", "Reintegrated with MangaDex automatically.", "MangaHub reader restored", "An admin restored MangaHub reader access for", cancellationToken);
        return match;
    }

    public async Task<bool> DismissAsync(Guid adminUserId, Guid issueId, DismissAdminIssueRequest request, CancellationToken cancellationToken)
    {
        var issue = await issues.GetDetailsAsync(issueId, cancellationToken);
        if (issue is null || issue.Status != "open") return false;
        await CloseAsync(issue, adminUserId, "dismissed", request.ResolutionNote, "External reader report reviewed", "An admin reviewed the external reader report for", cancellationToken);
        return true;
    }

    public async Task<bool> ReopenAsync(Guid adminUserId, Guid issueId, CancellationToken cancellationToken)
    {
        var issue = await issues.GetDetailsAsync(issueId, cancellationToken);
        if (issue is null || issue.Status == "open") return false;
        issue.Status = "open";
        issue.ResolvedAt = null;
        issue.ResolvedByUserId = null;
        issue.ResolutionNote = "";
        issue.UpdatedAt = DateTimeOffset.UtcNow;
        await issues.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task CloseAsync(AdminIssue issue, Guid adminUserId, string status, string note, string notificationTitle, string notificationPrefix, CancellationToken cancellationToken)
    {
        issue.Status = status;
        issue.ResolvedAt = DateTimeOffset.UtcNow;
        issue.ResolvedByUserId = adminUserId;
        issue.ResolutionNote = note.Trim()[..Math.Min(note.Trim().Length, 800)];
        issue.UpdatedAt = DateTimeOffset.UtcNow;
        foreach (var reporter in issue.Reports.Select(report => report.ReporterUserId).Distinct())
        {
            notifications.Add(new MangaNotification
            {
                UserId = reporter,
                MangaEntryId = issue.SubjectId,
                Type = $"issue-{issue.Id:N}",
                ChapterNumber = 0,
                Language = "",
                Title = notificationTitle,
                Body = $"{notificationPrefix} {issue.TitleSnapshot}."
            });
        }
        await issues.SaveChangesAsync(cancellationToken);
    }

    private static bool IsHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
