namespace MangaHub.Web.API.DTOs;

public sealed record WebPushSubscriptionRequest(string Endpoint, string P256dh, string Auth, string DeviceLabel = "");
