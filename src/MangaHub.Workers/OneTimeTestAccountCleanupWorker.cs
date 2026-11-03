using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MangaHub.Workers;

public sealed class OneTimeTestAccountCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MangaHubOptions> options,
    ILogger<OneTimeTestAccountCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
                var expired = await db.Users
                    .Where(user => user.TemporaryAccountExpiresAt != null && user.TemporaryAccountExpiresAt <= DateTimeOffset.UtcNow)
                    .ToListAsync(stoppingToken);

                if (expired.Count > 0)
                {
                    var userIds = expired.Select(user => user.Id).ToList();
                    db.PasswordResetTokens.RemoveRange(await db.PasswordResetTokens.Where(item => userIds.Contains(item.UserId)).ToListAsync(stoppingToken));
                    db.EmailVerificationTokens.RemoveRange(await db.EmailVerificationTokens.Where(item => userIds.Contains(item.UserId)).ToListAsync(stoppingToken));
                    db.Notifications.RemoveRange(await db.Notifications.Where(item => userIds.Contains(item.UserId)).ToListAsync(stoppingToken));
                    db.WebPushSubscriptions.RemoveRange(await db.WebPushSubscriptions.Where(item => userIds.Contains(item.UserId)).ToListAsync(stoppingToken));
                    db.UserMangaEntries.RemoveRange(await db.UserMangaEntries.Where(item => userIds.Contains(item.UserId)).ToListAsync(stoppingToken));
                    db.ReadingProgress.RemoveRange(await db.ReadingProgress.Where(item => userIds.Contains(item.UserId)).ToListAsync(stoppingToken));
                    db.Follows.RemoveRange(await db.Follows.Where(item => userIds.Contains(item.UserId)).ToListAsync(stoppingToken));
                    db.UsageEvents.RemoveRange(await db.UsageEvents.Where(item => userIds.Contains(item.UserId)).ToListAsync(stoppingToken));
                    db.UsageDailySummaries.RemoveRange(await db.UsageDailySummaries.Where(item => userIds.Contains(item.UserId)).ToListAsync(stoppingToken));
                    db.AdminIssueReports.RemoveRange(await db.AdminIssueReports.Where(item => userIds.Contains(item.ReporterUserId)).ToListAsync(stoppingToken));
                    db.MaintenanceJobs.RemoveRange(await db.MaintenanceJobs.Where(item => userIds.Contains(item.RequestedByUserId)).ToListAsync(stoppingToken));

                    foreach (var manga in await db.MangaEntries.Where(item => item.CreatedByUserId != null && userIds.Contains(item.CreatedByUserId.Value)).ToListAsync(stoppingToken))
                    {
                        manga.CreatedByUserId = null;
                    }
                    foreach (var issue in await db.AdminIssues.Where(item => item.ResolvedByUserId != null && userIds.Contains(item.ResolvedByUserId.Value)).ToListAsync(stoppingToken))
                    {
                        issue.ResolvedByUserId = null;
                    }

                    db.Users.RemoveRange(expired);
                    await db.SaveChangesAsync(stoppingToken);
                    logger.LogInformation("Deleted {Count} expired one-time test account(s).", expired.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "One-time test account cleanup failed; it will retry on the next run.");
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(options.Value.OneTimeTestAccountCleanupIntervalMinutes, 1, 60)), stoppingToken);
        }
    }
}
