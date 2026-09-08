using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MangaHub.Workers;

public sealed class NotificationCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MangaHubOptions> options,
    ILogger<NotificationCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var retentionDays = Math.Clamp(options.Value.ReadNotificationRetentionDays, 1, 365);
                var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
                var expired = await db.Notifications
                    .Where(notification => notification.ReadAt != null
                        && notification.ReadAt <= cutoff
                        && db.Users.Any(user => user.Id == notification.UserId && user.AutoDeleteReadNotifications))
                    .ToListAsync(stoppingToken);
                var removed = expired.Count;
                if (removed > 0)
                {
                    db.Notifications.RemoveRange(expired);
                    await db.SaveChangesAsync(stoppingToken);
                }
                if (removed > 0)
                {
                    logger.LogInformation("Removed {Count} read notifications older than {RetentionDays} days.", removed, retentionDays);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Read notification cleanup failed; it will retry on the next run.");
            }

            var interval = Math.Clamp(options.Value.NotificationCleanupIntervalHours, 1, 168);
            await Task.Delay(TimeSpan.FromHours(interval), stoppingToken);
        }
    }
}
