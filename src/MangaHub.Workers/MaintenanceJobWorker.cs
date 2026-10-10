using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MangaHub.Workers;

public sealed class MaintenanceJobWorker(
    IServiceScopeFactory scopeFactory,
    InternalMaintenanceApiClient maintenanceApi,
    IOptions<MangaHubOptions> options,
    ILogger<MaintenanceJobWorker> logger) : BackgroundService
{
    private readonly Dictionary<Guid, RunningJob> runningJobs = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedJobsAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                RemoveCompletedJobs();
                await StartQueuedJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Maintenance queue check failed; it will retry shortly.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task StartQueuedJobsAsync(CancellationToken cancellationToken)
    {
        var maxConcurrency = Math.Clamp(options.Value.MaintenanceJobMaxConcurrency, 1, 4);
        while (runningJobs.Count < maxConcurrency)
        {
            var claim = await TryClaimNextQueuedJobAsync(cancellationToken);
            if (claim is null)
            {
                return;
            }

            var task = RunClaimedJobAsync(claim, cancellationToken);
            runningJobs.Add(claim.Id, new RunningJob(claim.Type, task));
        }
    }

    private async Task<ClaimedJob?> TryClaimNextQueuedJobAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
        var mangaDexLaneBusy = runningJobs.Values.Any(job => UsesMangaDexLane(job.Type));
        var queuedJobs = await db.MaintenanceJobs
            .Where(job => job.Status == "queued")
            .OrderBy(job => job.RequestedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        var job = queuedJobs.FirstOrDefault(candidate => !UsesMangaDexLane(candidate.Type) || !mangaDexLaneBusy);
        if (job is null)
        {
            return null;
        }

        job.Status = "running";
        job.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new ClaimedJob(job.Id, job.Type);
    }

    private async Task RunClaimedJobAsync(ClaimedJob claim, CancellationToken cancellationToken)
    {
        var status = "completed";
        var error = "";
        var result = new MaintenanceRunResult();
        try
        {
            result = await maintenanceApi.RunAsync(claim.Type, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave the job running. Startup recovery records the interruption and queues a retry.
            return;
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Maintenance job {JobId} ({Type}) timed out or was canceled.", claim.Id, claim.Type);
            status = "failed";
            error = "The internal maintenance request timed out or was canceled.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Maintenance job {JobId} ({Type}) failed.", claim.Id, claim.Type);
            status = "failed";
            error = ex.Message.Length <= 500 ? ex.Message : ex.Message[..500];
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
        var job = await db.MaintenanceJobs.FirstOrDefaultAsync(item => item.Id == claim.Id, CancellationToken.None);
        if (job is null || job.Status != "running")
        {
            return;
        }

        job.Status = status;
        job.Error = error;
        job.CompletedAt = DateTimeOffset.UtcNow;
        if (status == "completed"
            && result.ShouldContinue
            && string.Equals(claim.Type, "mangadex-cache-cleanup", StringComparison.Ordinal))
        {
            var continuationCutoff = DateTimeOffset.UtcNow.AddHours(-24);
            var continuationLimit = Math.Clamp(options.Value.MangaDexCacheRetentionMaxContinuationBatches, 1, 100);
            var continuationCount = await db.MaintenanceJobs.CountAsync(item =>
                item.Type == claim.Type
                && item.Trigger == "continuation"
                && item.RequestedAt >= continuationCutoff,
                CancellationToken.None);
            var continuationAlreadyQueued = await db.MaintenanceJobs.AnyAsync(item =>
                item.Id != claim.Id
                && item.Type == claim.Type
                && (item.Status == "queued" || item.Status == "running"), CancellationToken.None);
            if (continuationAlreadyQueued)
            {
                logger.LogInformation("Cache cleanup batch {JobId} yielded while another cleanup is pending; no duplicate continuation was queued.", claim.Id);
            }
            else if (continuationCount >= continuationLimit)
            {
                error = $"Stopped after {continuationLimit} cleanup continuations in 24 hours. The next daily maintenance run will resume remaining work.";
                job.Error = error;
                logger.LogWarning("Cache cleanup batch {JobId} reached its 24-hour continuation cap of {ContinuationLimit}.", claim.Id, continuationLimit);
            }
            else
            {
                db.MaintenanceJobs.Add(new MaintenanceJob
                {
                    Type = claim.Type,
                    Trigger = "continuation",
                    RequestedAt = DateTimeOffset.UtcNow
                });
                logger.LogInformation("Cache cleanup batch {JobId} yielded with more work; queued a continuation.", claim.Id);
            }
        }
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private void RemoveCompletedJobs()
    {
        foreach (var job in runningJobs.Where(item => item.Value.Task.IsCompleted).Select(item => item.Key).ToList())
        {
            runningJobs.Remove(job);
        }
    }

    private async Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
        var interrupted = await db.MaintenanceJobs
            .Where(job => job.Status == "running")
            .ToListAsync(cancellationToken);
        if (interrupted.Count == 0)
        {
            return;
        }

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

    private static bool UsesMangaDexLane(string type) => type is
        "release-sync" or
        "mangadex-status-sync" or
        "mangadex-language-coverage-check" or
        "prefetch" or
        "mangadex-cache-cleanup" or
        "mangadex-archive-integrity-check" or
        "idle-backfill";

    private sealed record ClaimedJob(Guid Id, string Type);
    private sealed record RunningJob(string Type, Task Task);
}
