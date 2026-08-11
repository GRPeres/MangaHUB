using MangaHub.Api.Services;
using Microsoft.AspNetCore.Mvc;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebPush;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController(CurrentUserService currentUsers, NotificationService notifications, UsageTrackingService usage, MangaHubDbContext db, IOptions<MangaHubOptions> options) : ControllerBase
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
        if (subscription is null) db.WebPushSubscriptions.Add(new WebPushSubscription { UserId = user.Id, Endpoint = request.Endpoint, P256dh = request.P256dh, Auth = request.Auth, DeviceLabel = request.DeviceLabel.Trim() });
        else { subscription.UserId = user.Id; subscription.P256dh = request.P256dh; subscription.Auth = request.Auth; subscription.DeviceLabel = request.DeviceLabel.Trim(); subscription.UpdatedAt = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("push/subscriptions")] public async Task<IActionResult> Subscriptions(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await db.WebPushSubscriptions.AsNoTracking().Where(subscription => subscription.UserId == user.Id)
            .OrderByDescending(subscription => subscription.UpdatedAt)
            .Select(subscription => new WebPushSubscriptionResponse(subscription.Id, subscription.DeviceLabel, subscription.UpdatedAt)).ToListAsync(cancellationToken));
    }

    [HttpGet("push/subscriptions/status")] public async Task<IActionResult> SubscriptionStatus(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await db.WebPushSubscriptions.AnyAsync(subscription => subscription.UserId == user.Id, cancellationToken));
    }

    [HttpDelete("push/subscriptions/{subscriptionId:guid}")] public async Task<IActionResult> Unsubscribe(Guid subscriptionId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        var subscription = await db.WebPushSubscriptions.FirstOrDefaultAsync(item => item.Id == subscriptionId && item.UserId == user.Id, cancellationToken);
        if (subscription is null) return NotFound();
        db.WebPushSubscriptions.Remove(subscription);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("push/test")] public async Task<IActionResult> TestPush(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var push = options.Value.WebPush;
        if (string.IsNullOrWhiteSpace(push.PublicKey) || string.IsNullOrWhiteSpace(push.PrivateKey)) return Ok(new DiagnosticResult(false, "Web Push is not configured."));
        var subscriptions = await db.WebPushSubscriptions.Where(subscription => subscription.UserId == user.Id).ToListAsync(cancellationToken);
        if (subscriptions.Count == 0) return Ok(new DiagnosticResult(false, "This account has no phone notification subscription."));
        var client = new WebPushClient();
        var payload = System.Text.Json.JsonSerializer.Serialize(new { title = "MangaHub test", body = "Phone notifications are working.", url = "/library" });
        var delivered = 0;
        var failed = 0;
        foreach (var subscription in subscriptions)
        {
            try
            {
                await client.SendNotificationAsync(new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth), payload, new VapidDetails(push.Subject, push.PublicKey, push.PrivateKey));
                delivered++;
            }
            catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound)
            {
                db.WebPushSubscriptions.Remove(subscription);
                failed++;
            }
            catch (WebPushException)
            {
                failed++;
            }
            catch (Exception)
            {
                failed++;
            }
        }
        db.Notifications.Add(new MangaNotification
        {
            UserId = user.Id,
            MangaEntryId = Guid.Empty,
            Type = $"test-{Guid.NewGuid():N}",
            ChapterNumber = 0,
            Language = user.PreferredLanguage,
            Title = "MangaHub notification test",
            Body = $"Push delivery accepted by {delivered} device(s); {failed} failed."
        });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new DiagnosticResult(delivered > 0, $"Saved an in-app test notification. Push delivery: {delivered} accepted, {failed} failed."));
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
        if (!await notifications.MarkReadAsync(user.Id, notificationId, cancellationToken)) return NotFound();
        await usage.TrackAsync(user.Id, UsageEventTypes.NotificationOpened, null, cancellationToken);
        return NoContent();
    }

    [HttpPost("read")] public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await notifications.MarkAllReadAsync(user.Id, cancellationToken));
    }

    [HttpDelete("read")] public async Task<IActionResult> ClearRead(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await notifications.ClearReadAsync(user.Id, cancellationToken));
    }
}
