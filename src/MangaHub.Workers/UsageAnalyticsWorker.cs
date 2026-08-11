using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Workers;

public sealed class UsageAnalyticsWorker(IServiceScopeFactory scopeFactory, ILogger<UsageAnalyticsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await AggregateAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Usage analytics aggregation failed; raw events will be retained for retry."); }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task AggregateAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
        var events = await db.UsageEvents.Where(item => item.OccurredAt < DateTimeOffset.UtcNow.Date).ToListAsync(cancellationToken);
        foreach (var group in events.GroupBy(item => new { item.UserId, Date = DateOnly.FromDateTime(item.OccurredAt.UtcDateTime) }))
        {
            var summary = await db.UsageDailySummaries.FirstOrDefaultAsync(item => item.UserId == group.Key.UserId && item.Date == group.Key.Date, cancellationToken);
            if (summary is null)
            {
                summary = new UsageDailySummary { UserId = group.Key.UserId, Date = group.Key.Date };
                db.UsageDailySummaries.Add(summary);
            }
            summary.ReaderSeconds = group.Where(item => item.EventType == UsageEventTypes.ReaderSession).Sum(item => item.DurationSeconds ?? 0);
            summary.ChaptersCompleted = group.Count(item => item.EventType == UsageEventTypes.ChapterCompleted);
            summary.MangaStarted = group.Count(item => item.EventType == UsageEventTypes.MangaStarted);
            summary.MangaCompleted = group.Count(item => item.EventType == UsageEventTypes.MangaCompleted);
            summary.ShelfChanges = group.Count(item => item.EventType.StartsWith("shelf.", StringComparison.Ordinal));
            summary.CatalogChanges = group.Count(item => item.EventType.StartsWith("catalog.", StringComparison.Ordinal));
            summary.Searches = group.Count(item => item.EventType == UsageEventTypes.Search);
            summary.NotificationOpens = group.Count(item => item.EventType == UsageEventTypes.NotificationOpened);
            summary.SignIns = group.Count(item => item.EventType == UsageEventTypes.SignIn);
            summary.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);

        var retentionCutoff = DateTimeOffset.UtcNow.AddDays(-90);
        var removable = await db.UsageEvents.Where(item => item.OccurredAt < retentionCutoff).ToListAsync(cancellationToken);
        if (removable.Count > 0)
        {
            db.UsageEvents.RemoveRange(removable);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Rolled up and removed {Count} expired usage events.", removable.Count);
        }
    }
}
