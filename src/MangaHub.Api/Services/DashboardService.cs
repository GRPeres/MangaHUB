using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;

namespace MangaHub.Api.Services;

public sealed class DashboardService(DashboardRepository dashboard)
{
    public Task<HomeDashboardResponse> GetAsync(Guid userId, string preferredLanguage, CancellationToken cancellationToken) =>
        dashboard.GetAsync(userId, preferredLanguage, cancellationToken);
}
