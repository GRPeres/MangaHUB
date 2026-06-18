using MangaHub.Core.Services;

namespace MangaHub.Workers;

public sealed class LibraryScanWorker(IServiceScopeFactory scopeFactory, ILogger<LibraryScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunScanAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunScanAsync(stoppingToken);
        }
    }

    private async Task RunScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var scanner = scope.ServiceProvider.GetRequiredService<ILibraryScanner>();
            var result = await scanner.ScanAsync(cancellationToken);
            logger.LogInformation("Library scan completed: {SeriesCount} series, {ChapterCount} chapters.", result.SeriesCount, result.ChapterCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Library scan failed.");
        }
    }
}

