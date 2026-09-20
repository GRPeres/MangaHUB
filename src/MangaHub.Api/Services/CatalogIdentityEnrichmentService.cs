using MangaHub.Api.Repositories;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Services;

/// <summary>
/// Repairs optional source IDs after catalog saves without making the admin form wait on remote providers.
/// </summary>
public sealed class CatalogIdentityEnrichmentService(
    MangaHubDbContext db,
    CatalogRepository catalog,
    MangaDexCatalogMatchService mangaDexMatches,
    MangaDexTitleMatchService mangaDexTitleMatches,
    MangaUpdatesCatalogMatchService mangaUpdatesMatches,
    IOptions<MangaHubOptions> options,
    ILogger<CatalogIdentityEnrichmentService> logger)
{
    public const string JobType = "catalog-id-enrichment";
    private static readonly SemaphoreSlim QueueLock = new(1, 1);
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    public async Task QueueAsync(Guid requestedByUserId, CancellationToken cancellationToken)
    {
        await QueueLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await db.MaintenanceJobs
                .FirstOrDefaultAsync(job => job.Type == JobType && (job.Status == "queued" || job.Status == "running"), cancellationToken);
            if (existing is not null)
            {
                return;
            }

            db.MaintenanceJobs.Add(new MaintenanceJob
            {
                Type = JobType,
                RequestedByUserId = requestedByUserId
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            QueueLock.Release();
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await RunLock.WaitAsync(cancellationToken);
        try
        {
            var retryCutoff = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(options.Value.MangaUpdatesMatchRetryHours, 6, 24 * 30));
            var batchSize = Math.Clamp(options.Value.MangaUpdatesMatchBatchSize, 1, 50);
            var entries = await db.MangaEntries
                .Where(entry =>
                    (entry.MangaDexId == "" && entry.Title != "" &&
                     (entry.MangaDexLastMatchAttemptAt == null || entry.MangaDexLastMatchAttemptAt < retryCutoff)) ||
                    (entry.MangaUpdatesId == "" &&
                     (entry.MangaUpdatesLastMatchAttemptAt == null || entry.MangaUpdatesLastMatchAttemptAt < retryCutoff)))
                .OrderByDescending(entry => entry.CreatedAt)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            var mangaDexMatchesFound = 0;
            var mangaUpdatesMatchesFound = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = DateTimeOffset.UtcNow;

                if (string.IsNullOrWhiteSpace(entry.MangaDexId))
                {
                    entry.MangaDexLastMatchAttemptAt = now;
                    var match = await mangaDexMatches.FindAsync(entry.MyAnimeListId, entry.Title, cancellationToken)
                        ?? await mangaDexTitleMatches.FindAsync(entry.Title, cancellationToken);
                    if (match is not null && await IsAvailableAsync(entry, match.Id, isMangaDex: true, cancellationToken))
                    {
                        entry.MangaDexId = match.Id;
                        entry.UpdatedAt = now;
                        mangaDexMatchesFound++;
                    }
                }

                if (string.IsNullOrWhiteSpace(entry.MangaUpdatesId))
                {
                    entry.MangaUpdatesLastMatchAttemptAt = now;
                    var match = await mangaUpdatesMatches.FindAsync(entry.Title, entry.MediaType, entry.FirstPublishYear, cancellationToken);
                    if (match is not null && await IsAvailableAsync(entry, match.Id, isMangaDex: false, cancellationToken))
                    {
                        entry.MangaUpdatesId = match.Id;
                        entry.UpdatedAt = now;
                        mangaUpdatesMatchesFound++;
                    }
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            logger.LogInformation(
                "Catalog identity enrichment checked {CheckedCount} entries and matched {MangaDexCount} MangaDex and {MangaUpdatesCount} MangaUpdates IDs.",
                entries.Count,
                mangaDexMatchesFound,
                mangaUpdatesMatchesFound);
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task<bool> IsAvailableAsync(MangaEntry entry, string id, bool isMangaDex, CancellationToken cancellationToken)
    {
        var existing = isMangaDex
            ? await catalog.FindByMangaDexIdAsync(id, cancellationToken)
            : await catalog.FindByMangaUpdatesIdAsync(id, cancellationToken);
        if (existing is null || existing.Id == entry.Id)
        {
            return true;
        }

        logger.LogWarning(
            "Skipped automatic {Provider} ID {ExternalId} for {Title}; it is already bound to {ExistingTitle}.",
            isMangaDex ? "MangaDex" : "MangaUpdates",
            id,
            entry.Title,
            existing.Title);
        return false;
    }
}
