using MangaHub.Web.API.DTOs;

namespace MangaHub.Web.API.Services;

public sealed class IssueApiService(ApiHttpClient api)
{
    public Task<AdminIssueReportStateResponse?> GetReportStateAsync(string kind, string subjectType, Guid subjectId) =>
        api.GetAsync<AdminIssueReportStateResponse>($"/api/issues/report-state?kind={Uri.EscapeDataString(kind)}&subjectType={Uri.EscapeDataString(subjectType)}&subjectId={subjectId}");

    public Task<AdminIssueReportStateResponse?> ReportAsync(CreateAdminIssueReportRequest request) =>
        api.SendAsync<CreateAdminIssueReportRequest, AdminIssueReportStateResponse>(HttpMethod.Post, "/api/issues/reports", request);
}
