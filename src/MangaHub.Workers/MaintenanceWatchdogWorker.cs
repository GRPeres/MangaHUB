namespace MangaHub.Workers;

public sealed class MaintenanceWatchdogWorker(
    InternalMaintenanceApiClient maintenanceApi,
    ILogger<MaintenanceWatchdogWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                await maintenanceApi.RunWatchdogAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Maintenance watchdog check failed; it will retry in 15 minutes.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
