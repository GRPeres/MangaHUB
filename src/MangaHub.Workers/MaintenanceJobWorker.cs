using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Workers;

public sealed class MaintenanceJobWorker(IServiceScopeFactory scopeFactory, RemoteSyncWorker remoteSync, ILogger<MaintenanceJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        do
        {
            try { await RunQueuedJobsAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Maintenance queue check failed; it will retry shortly."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunQueuedJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
        var job = await db.MaintenanceJobs.OrderBy(item => item.RequestedAt).FirstOrDefaultAsync(item => item.Status == "queued", cancellationToken);
        if (job is null) return;
        job.Status = "running";
        job.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            if (job.Type == "library-scan")
            {
                await scope.ServiceProvider.GetRequiredService<ILibraryScanner>().ScanAsync(cancellationToken);
            }
            else
            {
                await remoteSync.RunRequestedAsync(job.Type, cancellationToken);
            }
            job.Status = "completed";
            job.Error = "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Maintenance job {JobId} ({Type}) failed.", job.Id, job.Type);
            job.Status = "failed";
            job.Error = ex.Message.Length <= 500 ? ex.Message : ex.Message[..500];
        }
        finally
        {
            job.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
