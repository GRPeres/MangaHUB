using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Services;

public sealed class DashboardService(DashboardRepository dashboard, IOptions<MangaHubOptions> options)
{
    public Task<HomeDashboardResponse> GetAsync(Guid userId, string preferredLanguage, CancellationToken cancellationToken) =>
        dashboard.GetAsync(
            userId,
            preferredLanguage,
            DateTimeOffset.UtcNow.AddDays(-Math.Clamp(options.Value.ExternalReaderCheckIntervalDays, 1, 90)),
            cancellationToken);
}
