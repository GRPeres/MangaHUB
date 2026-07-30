using MangaHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController(CurrentUserService currentUsers, NotificationService notifications, MangaHubDbContext db, IOptions<MangaHubOptions> options) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await notifications.ListAsync(user.Id, cancellationToken));
    }

    [HttpGet("push/public-key")] public IActionResult PublicKey() => string.IsNullOrWhiteSpace(options.Value.WebPush.PublicKey) ? NotFound() : Ok(new { publicKey = options.Value.WebPush.PublicKey });

    [HttpPost("push/subscriptions")] public async Task<IActionResult> Subscribe([FromBody] WebPushSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Endpoint) || string.IsNullOrWhiteSpace(request.P256dh) || string.IsNullOrWhiteSpace(request.Auth)) return BadRequest();
        var subscription = await db.WebPushSubscriptions.FirstOrDefaultAsync(item => item.Endpoint == request.Endpoint, cancellationToken);
        if (subscription is null) db.WebPushSubscriptions.Add(new WebPushSubscription { UserId = user.Id, Endpoint = request.Endpoint, P256dh = request.P256dh, Auth = request.Auth });
        else { subscription.UserId = user.Id; subscription.P256dh = request.P256dh; subscription.Auth = request.Auth; subscription.UpdatedAt = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("push/subscriptions/status")] public async Task<IActionResult> SubscriptionStatus(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await db.WebPushSubscriptions.AnyAsync(subscription => subscription.UserId == user.Id, cancellationToken));
    }

    [HttpGet("unread-count")] public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await notifications.UnreadCountAsync(user.Id, cancellationToken));
    }

    [HttpPost("{notificationId:guid}/read")] public async Task<IActionResult> MarkRead(Guid notificationId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        return await notifications.MarkReadAsync(user.Id, notificationId, cancellationToken) ? NoContent() : NotFound();
    }
}
