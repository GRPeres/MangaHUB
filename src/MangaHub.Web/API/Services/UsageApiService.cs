using MangaHub.Web.API.DTOs;

namespace MangaHub.Web.API.Services;

public sealed record UsageTelemetryRequest(string EventType, Guid? MangaEntryId = null, Guid? ChapterId = null, string SessionId = "", string IdempotencyKey = "", int? DurationSeconds = null);

public sealed class UsageApiService(ApiHttpClient api)
{
    public Task<UserResponse?> SetEnabledAsync(bool enabled) => api.SendAsync<object, UserResponse>(HttpMethod.Put, "/api/usage/preferences", new { enabled });
    public Task TrackAsync(UsageTelemetryRequest request) => api.SendWithoutResponseAsync(HttpMethod.Post, "/api/usage/events", request);
    public Task<bool> DeleteAsync() => api.DeleteAsync("/api/usage");
    public string ExportUrl => api.GetAbsoluteUrl("/api/usage/export");
}
