using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Workers;

public sealed class RemoteMaintenanceScheduleWorker(
    InternalMaintenanceApiClient maintenanceApi,
    IOptions<MangaHubOptions> options,
    ILogger<RemoteMaintenanceScheduleWorker> logger) : BackgroundService
{
    private readonly object scheduledJobLock = new();
    private readonly Dictionary<string, Task> runningScheduledJobs = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextReleaseSyncAt = DateTimeOffset.MinValue;
        var nextPrefetchAt = DateTimeOffset.MinValue;
        var nextMangaUpdatesSyncAt = DateTimeOffset.MinValue;
        var nextMangaUpdatesMatchAt = DateTimeOffset.MinValue;
        var nextLibraryScanAt = DateTimeOffset.MinValue;
        var nextIdleBackfillAt = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextReleaseSyncAt)
            {
                StartScheduledJob("release-sync", token => DispatchAsync("release-sync", token), stoppingToken);
                nextReleaseSyncAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.Value.MangaDexReleasePollMinutes, 15, 720));
            }
            if (now >= nextPrefetchAt)
            {
                StartScheduledJob("daily-cache-maintenance", async token =>
                {
                    await DispatchAsync("prefetch", token);
                    await DispatchAsync("mangadex-cache-cleanup", token);
                }, stoppingToken);
                nextPrefetchAt = DateTimeOffset.UtcNow.Add(GetDelayUntilNextMaintenance());
            }
            if (now >= nextMangaUpdatesMatchAt)
            {
                StartScheduledJob("catalog-id-enrichment", token => DispatchAsync("catalog-id-enrichment", token), stoppingToken);
                nextMangaUpdatesMatchAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.Value.MangaUpdatesMatchPollMinutes, 5, 720));
            }
            if (now >= nextMangaUpdatesSyncAt)
            {
                StartScheduledJob("mangaupdates-sync", token => DispatchAsync("mangaupdates-sync", token), stoppingToken);
                nextMangaUpdatesSyncAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.Value.MangaUpdatesReleasePollMinutes, 15, 720));
            }
            if (now >= nextLibraryScanAt)
            {
                StartScheduledJob("library-scan", token => DispatchAsync("library-scan", token), stoppingToken);
                nextLibraryScanAt = DateTimeOffset.UtcNow.AddHours(1);
            }
            if (now >= nextIdleBackfillAt)
            {
                StartScheduledJob("idle-backfill", token => DispatchAsync("idle-backfill", token), stoppingToken);
                nextIdleBackfillAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.Value.MangaDexIdleBackfillCheckMinutes, 5, 720));
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private void StartScheduledJob(string name, Func<CancellationToken, Task> operation, CancellationToken stoppingToken)
    {
        lock (scheduledJobLock)
        {
            if (runningScheduledJobs.TryGetValue(name, out var existingJob) && !existingJob.IsCompleted)
            {
                logger.LogInformation("Scheduled maintenance job {Name} is still running; skipping overlapping dispatch.", name);
                return;
            }

            logger.LogInformation("Starting scheduled maintenance job {Name}.", name);
            runningScheduledJobs[name] = RunScheduledJobAsync(name, operation, stoppingToken);
        }
    }

    private async Task RunScheduledJobAsync(string name, Func<CancellationToken, Task> operation, CancellationToken stoppingToken)
    {
        try
        {
            await operation(stoppingToken);
            logger.LogInformation("Scheduled maintenance job {Name} completed.", name);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Scheduled maintenance job {Name} was cancelled during shutdown.", name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Scheduled maintenance job {Name} failed.", name);
        }
    }

    private async Task DispatchAsync(string type, CancellationToken cancellationToken)
    {
        try
        {
            await maintenanceApi.RunAsync(type, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not dispatch scheduled maintenance job {Type}; it will retry on the next schedule.", type);
        }
    }

    private TimeSpan GetDelayUntilNextMaintenance()
    {
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.MangaDexMaintenanceTimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            logger.LogWarning("MangaDex maintenance timezone {TimeZone} was not found. Falling back to UTC.", options.Value.MangaDexMaintenanceTimeZone);
            timeZone = TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            logger.LogWarning("MangaDex maintenance timezone {TimeZone} is invalid. Falling back to UTC.", options.Value.MangaDexMaintenanceTimeZone);
            timeZone = TimeZoneInfo.Utc;
        }
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        var localTarget = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day,
            Math.Clamp(options.Value.MangaDexMaintenanceHour, 0, 23), 0, 0, localNow.Offset);
        if (localTarget <= localNow)
        {
            localTarget = localTarget.AddDays(1);
        }
        return localTarget.ToUniversalTime() - DateTimeOffset.UtcNow;
    }
}
