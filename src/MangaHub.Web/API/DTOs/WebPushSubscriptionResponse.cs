namespace MangaHub.Web.API.DTOs;

public sealed record WebPushSubscriptionResponse(Guid Id, string DeviceLabel, DateTimeOffset UpdatedAt);
