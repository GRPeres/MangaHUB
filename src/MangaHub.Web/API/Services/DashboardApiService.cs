using MangaHub.Web.API.DTOs;

namespace MangaHub.Web.API.Services;

public sealed class DashboardApiService(ApiHttpClient api)
{
    public Task<HomeDashboardResponse?> GetAsync() => api.GetAsync<HomeDashboardResponse>("/api/dashboard");
}
