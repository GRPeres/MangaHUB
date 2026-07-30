using MangaHub.Web.API.DTOs;

namespace MangaHub.Web.API.Services;

public sealed class NotificationApiService(ApiHttpClient api)
{
    public Task<List<MangaNotificationResponse>?> GetAsync() => api.GetAsync<List<MangaNotificationResponse>>("/api/notifications");
    public Task<int> GetUnreadCountAsync() => api.GetAsync<int>("/api/notifications/unread-count");
    public Task<bool> MarkReadAsync(Guid id) => api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/notifications/{id}/read", new { });
    public async Task<string?> GetPushPublicKeyAsync() => (await api.GetAsync<WebPushPublicKeyResponse>("/api/notifications/push/public-key"))?.PublicKey;
    public Task<bool> SubscribeToPushAsync(WebPushSubscriptionRequest request) => api.SendWithoutResponseAsync(HttpMethod.Post, "/api/notifications/push/subscriptions", request);
    public Task<bool> IsPushEnabledAsync() => api.GetAsync<bool>("/api/notifications/push/subscriptions/status");
}
