using MangaHub.Web.API.DTOs;

namespace MangaHub.Web.API.Services;

public sealed class AdminApiService(ApiHttpClient api)
{
    public async Task<List<UserAdminResponse>> GetUsersAsync() =>
        await api.GetAsync<List<UserAdminResponse>>("/api/admin/users") ?? [];

    public async Task<UserAdminResponse?> UpdateUserRoleAsync(Guid userId, string role) =>
        await api.SendAsync<UpdateUserRoleRequest, UserAdminResponse>(HttpMethod.Put, $"/api/admin/users/{userId}/role", new(role));

    public Task<bool> DeleteTestAccountAsync(Guid userId) =>
        api.SendWithoutResponseAsync(HttpMethod.Delete, $"/api/admin/users/{userId}", new { });

    public Task<ApiCallResult<OneTimeTestAccountCredentialsResponse>> CreateOneTimeTestAccountAsync() =>
        api.SendWithResultAsync<object, OneTimeTestAccountCredentialsResponse>(HttpMethod.Post, "/api/admin/test-accounts", new { });

    public Task<DiagnosticResult?> TestDatabaseAsync() => api.GetAsync<DiagnosticResult>("/api/admin/diagnostics/database");
    public Task<DiagnosticResult?> TestMangaDexAsync() => api.GetAsync<DiagnosticResult>("/api/admin/diagnostics/mangadex");
    public Task<OperationsOverviewResponse?> GetOperationsAsync() => api.GetAsync<OperationsOverviewResponse>("/api/admin/operations");
    public Task<ArchiveOverviewResponse?> GetArchiveOverviewAsync() => api.GetAsync<ArchiveOverviewResponse>("/api/admin/archive");
    public Task<ArchiveRecoveryTestResponse?> TestArchiveRecoveryAsync() => api.SendAsync<object, ArchiveRecoveryTestResponse>(HttpMethod.Post, "/api/admin/archive/test-recovery", new { });
    public Task<int> GetCatalogCountAsync() => api.GetAsync<int>("/api/admin/catalog/count");
    public async Task<List<MaintenanceJobResponse>> GetMaintenanceHistoryAsync(int offset, int limit = 25, string? type = null, string? status = null, string? trigger = null)
    {
        var filters = new[]
        {
            $"offset={Math.Max(0, offset)}",
            $"limit={Math.Clamp(limit, 1, 100)}",
            string.IsNullOrWhiteSpace(type) ? "" : $"type={Uri.EscapeDataString(type)}",
            string.IsNullOrWhiteSpace(status) ? "" : $"status={Uri.EscapeDataString(status)}",
            string.IsNullOrWhiteSpace(trigger) ? "" : $"trigger={Uri.EscapeDataString(trigger)}"
        };
        return await api.GetAsync<List<MaintenanceJobResponse>>($"/api/admin/operations/jobs?{string.Join("&", filters.Where(filter => filter.Length > 0))}") ?? [];
    }
    public Task<MaintenanceJobResponse?> QueueMaintenanceJobAsync(string type) => api.SendAsync<object, MaintenanceJobResponse>(HttpMethod.Post, "/api/admin/operations/jobs", new { type });
    public Task<int> GetOpenIssueCountAsync() => api.GetAsync<int>("/api/admin/issues/open-count");
    public sealed record IssueProposal(string Text);
    public sealed record ChatGptStatus(bool Enabled, bool CredentialPresent, DateTimeOffset? LastVerifiedAt);
    public Task<ChatGptStatus?> GetChatGptStatusAsync() => api.GetAsync<ChatGptStatus>("/api/admin/issues/chatgpt-status");
    public Task<ApiCallResult<ChatGptStatus>> TestChatGptConnectionAsync() =>
        api.SendWithResultAsync<object, ChatGptStatus>(HttpMethod.Post, "/api/admin/issues/chatgpt-test", new { });
    public sealed record ScheduledDraft(Guid Id, string Text, bool Manual = false);
    public sealed record ProposalSchedule(bool Enabled, int BatchIntervalMinutes, int RequestIntervalSeconds,
        int GroupSize, DateTimeOffset? NextRunAt, bool Running, string LastError, List<ScheduledDraft> Drafts,
        bool ChatGptConfigured = false, bool ChatGptCredentialPresent = false, bool WebResearchEnabled = true);
    public Task<ProposalSchedule?> GetProposalScheduleAsync() => api.GetAsync<ProposalSchedule>("/api/admin/issues/proposal-schedule");
    public Task<bool> SaveProposalScheduleAsync(bool enabled, int batchIntervalMinutes, int requestIntervalSeconds, int groupSize, bool webResearchEnabled) =>
        api.SendWithoutResponseAsync(HttpMethod.Post, "/api/admin/issues/proposal-schedule",
            new { enabled, batchIntervalMinutes, requestIntervalSeconds, groupSize, webResearchEnabled });
    public Task<bool> ReviewScheduledDraftAsync(Guid id) =>
        api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/admin/issues/proposal-schedule/{id}/review", new { });
    public Task<ApiCallResult<IssueProposal>> SuggestIssueRepairsAsync(Guid[] ids) =>
        api.SendWithResultAsync<Guid[], IssueProposal>(HttpMethod.Post, "/api/admin/issues/suggest-repairs", ids);
    public Task<ApiCallResult<IssueProposal>> SuggestIssueRepairAsync(Guid id) =>
        api.SendWithResultAsync<object, IssueProposal>(HttpMethod.Post, $"/api/admin/issues/{id}/suggest-repair", new { });
    public Task<IssueHealthResponse?> GetIssueHealthAsync() => api.GetAsync<IssueHealthResponse>("/api/admin/issues/health");
    public Task<int> ReviewIssuesAsync(Guid[] ids, string action, int days) =>
        api.SendAsync<object, int>(HttpMethod.Post, "/api/admin/issues/batch", new { issueIds = ids, action, days });
    public async Task<List<AdminIssueListItemResponse>> GetIssuesAsync(string status = "open", int offset = 0, int limit = 40) =>
        await api.GetAsync<List<AdminIssueListItemResponse>>($"/api/admin/issues?status={Uri.EscapeDataString(status)}&offset={offset}&limit={limit}") ?? [];
    public Task<AdminIssueDetailsResponse?> GetIssueAsync(Guid issueId) => api.GetAsync<AdminIssueDetailsResponse>($"/api/admin/issues/{issueId}");
    public Task<bool> ResolveIssueAsync(Guid issueId, ResolveAdminIssueRequest request) => api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/admin/issues/{issueId}/resolve", request);
    public Task<bool> RecordExternalReaderCoverageAsync(Guid issueId, RecordExternalReaderCoverageRequest request) => api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/admin/issues/{issueId}/record-external-coverage", request);
    public Task<bool> ResolveCoverImageIssueAsync(Guid issueId, ResolveCoverImageIssueRequest request) => api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/admin/issues/{issueId}/resolve-cover", request);
    public Task<ApiCallResult<MangaDexCatalogMatch>> ReintegrateIssueWithMangaDexAsync(Guid issueId) => api.SendWithResultAsync<object, MangaDexCatalogMatch>(HttpMethod.Post, $"/api/admin/issues/{issueId}/reintegrate-mangadex", new { });
    public Task<ApiCallResult<MangaDexCatalogMatch>> RepairMangaDexIdentityAsync(Guid issueId, bool preview, string expectedMalId, string? matchId = null) => api.SendWithResultAsync<object, MangaDexCatalogMatch>(HttpMethod.Post, $"/api/admin/issues/{issueId}/repair-mangadex-identity?preview={preview.ToString().ToLowerInvariant()}&confirmedMalId={Uri.EscapeDataString(expectedMalId)}{(matchId is null ? "" : $"&confirmedMatchId={Uri.EscapeDataString(matchId)}")}", new { });
    public Task<ApiCallResult<MangaDexCatalogMatch>> RepairMalIdentityAsync(Guid issueId, string expectedMalId, string linkedMalId) => api.SendWithResultAsync<object, MangaDexCatalogMatch>(HttpMethod.Post, $"/api/admin/issues/{issueId}/repair-mal-identity?expectedMalId={Uri.EscapeDataString(expectedMalId)}&linkedMalId={Uri.EscapeDataString(linkedMalId)}", new { });
    public Task<ApiCallResult<IssueMetadataReintegrationResponse>> ReintegrateIssueWithMetadataAsync(Guid issueId, ReintegrateIssueWithMetadataRequest request) => api.SendWithResultAsync<ReintegrateIssueWithMetadataRequest, IssueMetadataReintegrationResponse>(HttpMethod.Post, $"/api/admin/issues/{issueId}/reintegrate-metadata", request);
    public Task<ApiCallResult<CatalogMangaResponse>> ResolveCatalogRegistrationIssueAsync(Guid issueId, MangaEntryRequest request) => api.SendWithResultAsync<MangaEntryRequest, CatalogMangaResponse>(HttpMethod.Post, $"/api/admin/issues/{issueId}/resolve-catalog-registration", request);
    public Task<bool> MergeDuplicateCatalogIssueAsync(Guid issueId, Guid keepMangaEntryId) => api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/admin/issues/{issueId}/merge-duplicates", new MergeDuplicateCatalogIssueRequest(keepMangaEntryId));
    public Task<bool> DismissIssueAsync(Guid issueId, string note = "") => api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/admin/issues/{issueId}/dismiss", new DismissAdminIssueRequest(note));
    public Task<bool> ReopenIssueAsync(Guid issueId) => api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/admin/issues/{issueId}/reopen", new { });
}
