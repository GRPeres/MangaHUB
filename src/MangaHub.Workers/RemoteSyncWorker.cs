namespace MangaHub.Workers;

public sealed class RemoteSyncWorker(ILogger<RemoteSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            logger.LogInformation("Remote sync placeholder ran. Followed-series refresh will be implemented after MVP local reading.");
        }
    }
}

