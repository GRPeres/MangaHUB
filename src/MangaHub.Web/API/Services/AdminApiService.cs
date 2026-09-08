using MangaHub.Web.API.DTOs;

namespace MangaHub.Web.API.Services;

public sealed class AdminApiService(ApiHttpClient api)
{
    public async Task<List<UserAdminResponse>> GetUsersAsync() =>
        await api.GetAsync<List<UserAdminResponse>>("/api/admin/users") ?? [];

    public async Task<UserAdminResponse?> UpdateUserRoleAsync(Guid userId, string role) =>
        await api.SendAsync<UpdateUserRoleRequest, UserAdminResponse>(HttpMethod.Put, $"/api/admin/users/{userId}/role", new(role));

    public Task<DiagnosticResult?> TestDatabaseAsync() => api.GetAsync<DiagnosticResult>("/api/admin/diagnostics/database");
    public Task<DiagnosticResult?> TestMangaDexAsync() => api.GetAsync<DiagnosticResult>("/api/admin/diagnostics/mangadex");
    public Task<OperationsOverviewResponse?> GetOperationsAsync() => api.GetAsync<OperationsOverviewResponse>("/api/admin/operations");
    public Task<MaintenanceJobResponse?> QueueMaintenanceJobAsync(string type) => api.SendAsync<object, MaintenanceJobResponse>(HttpMethod.Post, "/api/admin/operations/jobs", new { type });
    public Task<int> GetOpenIssueCountAsync() => api.GetAsync<int>("/api/admin/issues/open-count");
    public async Task<List<AdminIssueListItemResponse>> GetIssuesAsync(string status = "open", int offset = 0, int limit = 40) =>
        await api.GetAsync<List<AdminIssueListItemResponse>>($"/api/admin/issues?status={Uri.EscapeDataString(status)}&offset={offset}&limit={limit}") ?? [];
    public Task<AdminIssueDetailsResponse?> GetIssueAsync(Guid issueId) => api.GetAsync<AdminIssueDetailsResponse>($"/api/admin/issues/{issueId}");
    public Task<bool> ResolveIssueAsync(Guid issueId, ResolveAdminIssueRequest request) => api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/admin/issues/{issueId}/resolve", request);
    public Task<ApiCallResult<MangaDexCatalogMatch>> ReintegrateIssueWithMangaDexAsync(Guid issueId) => api.SendWithResultAsync<object, MangaDexCatalogMatch>(HttpMethod.Post, $"/api/admin/issues/{issueId}/reintegrate-mangadex", new { });
    public Task<ApiCallResult<IssueMetadataReintegrationResponse>> ReintegrateIssueWithMetadataAsync(Guid issueId, ReintegrateIssueWithMetadataRequest request) => api.SendWithResultAsync<ReintegrateIssueWithMetadataRequest, IssueMetadataReintegrationResponse>(HttpMethod.Post, $"/api/admin/issues/{issueId}/reintegrate-metadata", request);
    public Task<bool> MergeDuplicateCatalogIssueAsync(Guid issueId, Guid keepMangaEntryId) => api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/admin/issues/{issueId}/merge-duplicates", new MergeDuplicateCatalogIssueRequest(keepMangaEntryId));
    public Task<bool> DismissIssueAsync(Guid issueId, string note = "") => api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/admin/issues/{issueId}/dismiss", new DismissAdminIssueRequest(note));
    public Task<bool> ReopenIssueAsync(Guid issueId) => api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/admin/issues/{issueId}/reopen", new { });
}
