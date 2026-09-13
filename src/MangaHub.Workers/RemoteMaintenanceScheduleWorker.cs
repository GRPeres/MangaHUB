using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Workers;

public sealed class RemoteMaintenanceScheduleWorker(
    InternalMaintenanceApiClient maintenanceApi,
    IOptions<MangaHubOptions> options,
    ILogger<RemoteMaintenanceScheduleWorker> logger) : BackgroundService
{
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
                await DispatchAsync("release-sync", stoppingToken);
                nextReleaseSyncAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.Value.MangaDexReleasePollMinutes, 15, 720));
            }
            if (now >= nextPrefetchAt)
            {
                await DispatchAsync("prefetch", stoppingToken);
                await DispatchAsync("mangadex-cache-cleanup", stoppingToken);
                nextPrefetchAt = DateTimeOffset.UtcNow.Add(GetDelayUntilNextMaintenance());
            }
            if (now >= nextMangaUpdatesMatchAt)
            {
                await DispatchAsync("mangaupdates-match", stoppingToken);
                nextMangaUpdatesMatchAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.Value.MangaUpdatesMatchPollMinutes, 5, 720));
            }
            if (now >= nextMangaUpdatesSyncAt)
            {
                await DispatchAsync("mangaupdates-sync", stoppingToken);
                nextMangaUpdatesSyncAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.Value.MangaUpdatesReleasePollMinutes, 15, 720));
            }
            if (now >= nextLibraryScanAt)
            {
                await DispatchAsync("library-scan", stoppingToken);
                nextLibraryScanAt = DateTimeOffset.UtcNow.AddHours(1);
            }
            if (now >= nextIdleBackfillAt)
            {
                await DispatchAsync("idle-backfill", stoppingToken);
                nextIdleBackfillAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.Value.MangaDexIdleBackfillCheckMinutes, 5, 720));
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
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
