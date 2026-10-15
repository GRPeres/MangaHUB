using System.Text.Json;
using MangaHub.Core.Models;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebPush;

namespace MangaHub.Api.Services;

/// <summary>
/// Delivers actionable maintenance alerts to administrators without making the
/// scheduled checks noisy. Each issue can alert once per urgency level.
/// </summary>
public sealed class AdminIssueNotificationService(
    MangaHubDbContext db,
    IOptions<MangaHubOptions> options,
    ILogger<AdminIssueNotificationService> logger)
{
    public async Task NotifyCoverageGapAsync(
        AdminIssue issue,
        MangaEntry manga,
        decimal maximumGap,
        string priority,
        CancellationToken cancellationToken)
    {
        if (priority is not ("high" or "critical"))
        {
            return;
        }

        // Notifications are already unique per user, manga, type, gap, and language.
        // Keep this compact because notifications.Type is deliberately limited to 40 characters.
        var type = $"adm-cov-{priority}";
        var alreadyNotified = await db.Notifications
            .Where(notification => notification.Type == type)
            .Select(notification => notification.UserId)
            .ToHashSetAsync(cancellationToken);
        var adminIds = await db.Users.AsNoTracking()
            .Where(user => user.Role == "admin")
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
        var label = priority == "critical" ? "Critical translation gap" : "Translation gap needs attention";

        foreach (var adminId in adminIds.Where(id => !alreadyNotified.Contains(id)))
        {
            var notification = new MangaNotification
            {
                UserId = adminId,
                MangaEntryId = manga.Id,
                Type = type,
                ChapterNumber = maximumGap,
                Language = "admin",
                Title = $"{label}: {manga.Title}",
                Body = $"Preferred MangaDex translations are {maximumGap:0.###} chapters behind the latest available release."
            };
            db.Notifications.Add(notification);
            await SendPushAsync(notification, cancellationToken);
        }
    }

    private async Task SendPushAsync(MangaNotification notification, CancellationToken cancellationToken)
    {
        var push = options.Value.WebPush;
        if (string.IsNullOrWhiteSpace(push.PublicKey) || string.IsNullOrWhiteSpace(push.PrivateKey))
        {
            return;
        }

        var subscriptions = await db.WebPushSubscriptions
            .Where(subscription => subscription.UserId == notification.UserId)
            .ToListAsync(cancellationToken);
        var payload = JsonSerializer.Serialize(new { title = notification.Title, body = notification.Body, url = "/admin/issues" });
        var client = new WebPushClient();

        foreach (var subscription in subscriptions)
        {
            try
            {
                await client.SendNotificationAsync(
                    new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                    payload,
                    new VapidDetails(push.Subject, push.PublicKey, push.PrivateKey));
            }
            catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound)
            {
                db.WebPushSubscriptions.Remove(subscription);
            }
            catch (WebPushException ex)
            {
                logger.LogWarning(ex, "Web push failed for maintenance notification {NotificationId}.", notification.Id);
            }
        }
    }
}
