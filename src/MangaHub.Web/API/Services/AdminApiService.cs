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
}
