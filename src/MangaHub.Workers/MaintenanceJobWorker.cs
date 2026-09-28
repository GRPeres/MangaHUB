using MangaHub.Core.Models;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Workers;

public sealed class MaintenanceJobWorker(IServiceScopeFactory scopeFactory, InternalMaintenanceApiClient maintenanceApi, ILogger<MaintenanceJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedJobsAsync(stoppingToken);

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
            await maintenanceApi.RunAsync(job.Type, cancellationToken);
            job.Status = "completed";
            job.Error = "";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Maintenance job {JobId} ({Type}) timed out or was canceled.", job.Id, job.Type);
            job.Status = "failed";
            job.Error = "The internal maintenance request timed out or was canceled.";
        }
        catch (Exception ex)
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

    private async Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
        var interrupted = await db.MaintenanceJobs
            .Where(job => job.Status == "running")
            .ToListAsync(cancellationToken);
        if (interrupted.Count == 0) return;

        foreach (var job in interrupted)
        {
            job.Status = "failed";
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Error = "Interrupted by a worker restart before the job completed. A recovery retry was queued.";
            db.MaintenanceJobs.Add(new MaintenanceJob
            {
                Type = job.Type,
                Trigger = "recovery",
                RequestedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Marked {Count} interrupted maintenance jobs as failed and queued recovery retries.", interrupted.Count);
    }
}
