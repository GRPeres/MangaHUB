using MangaHub.Web.API.DTOs;
using MangaHub.Web.Services;

namespace MangaHub.Web.API.Services;

public sealed class NotificationApiService(ApiHttpClient api, AppRefreshService refreshes)
{
    public Task<List<MangaNotificationResponse>?> GetAsync() => api.GetAsync<List<MangaNotificationResponse>>("/api/notifications");
    public Task<int> GetUnreadCountAsync() => api.GetAsync<int>("/api/notifications/unread-count");
    public async Task<bool> MarkReadAsync(Guid id)
    {
        var marked = await api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/notifications/{id}/read", new { });
        if (marked) refreshes.Notify(AppRefreshScope.Notifications | AppRefreshScope.Analytics);
        return marked;
    }
    public async Task<int> MarkAllReadAsync()
    {
        var marked = await api.SendAsync<object, int>(HttpMethod.Post, "/api/notifications/read", new { });
        if (marked > 0) refreshes.Notify(AppRefreshScope.Notifications);
        return marked;
    }
    public async Task<bool> ClearReadAsync()
    {
        var cleared = await api.DeleteAsync("/api/notifications/read");
        if (cleared) refreshes.Notify(AppRefreshScope.Notifications);
        return cleared;
    }
    public async Task<string?> GetPushPublicKeyAsync() => (await api.GetAsync<WebPushPublicKeyResponse>("/api/notifications/push/public-key"))?.PublicKey;
    public Task<bool> SubscribeToPushAsync(WebPushSubscriptionRequest request) => api.SendWithoutResponseAsync(HttpMethod.Post, "/api/notifications/push/subscriptions", request);
    public Task<bool> IsPushEnabledAsync() => api.GetAsync<bool>("/api/notifications/push/subscriptions/status");
    public Task<List<WebPushSubscriptionResponse>?> GetPushSubscriptionsAsync() => api.GetAsync<List<WebPushSubscriptionResponse>>("/api/notifications/push/subscriptions");
    public Task<bool> UnsubscribeFromPushAsync(Guid subscriptionId) => api.SendWithoutResponseAsync(HttpMethod.Delete, $"/api/notifications/push/subscriptions/{subscriptionId}", new { });
    public Task<DiagnosticResult?> SendTestPushAsync() => api.SendAsync<object, DiagnosticResult>(HttpMethod.Post, "/api/notifications/push/test", new { });
}
