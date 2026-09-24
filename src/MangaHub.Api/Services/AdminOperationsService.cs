using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Services;

public sealed class AdminOperationsService(MangaHubDbContext db, IOptions<MangaHubOptions> options)
{
    private static readonly HashSet<string> AllowedJobTypes = ["release-sync", "mangadex-status-sync", "prefetch", "mangadex-cache-cleanup", "mangaupdates-sync", CatalogIdentityEnrichmentService.JobType, "library-scan", "idle-backfill"];

    public async Task<OperationsOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = db.MangaEntries.AsNoTracking();
        var recentJobs = await db.MaintenanceJobs.AsNoTracking().OrderByDescending(job => job.RequestedAt).Take(12)
            .Select(job => new MaintenanceJobResponse(job.Id, job.Type, job.Trigger, job.Status, job.RequestedAt, job.StartedAt, job.CompletedAt, job.Error)).ToListAsync(cancellationToken);
        var cacheRoot = options.Value.MangaDexCachePath;
        var cacheUsage = GetCacheUsage(cacheRoot);
        return new OperationsOverviewResponse(
            await entries.CountAsync(cancellationToken),
            await entries.CountAsync(entry => entry.MangaDexId != "", cancellationToken),
            await entries.CountAsync(entry => entry.MangaUpdatesId != "", cancellationToken),
            cacheUsage.TotalChapters,
            cacheUsage.TotalBytes,
            cacheUsage.ActiveChapters,
            cacheUsage.ActiveBytes,
            cacheUsage.ArchivedChapters,
            cacheUsage.ArchivedBytes,
            await entries.MaxAsync(entry => entry.MangaDexLastSyncedAt, cancellationToken),
            await entries.MaxAsync(entry => entry.MangaUpdatesLastSyncedAt, cancellationToken),
            await db.MaintenanceJobs.AsNoTracking().Where(job => job.Type == "library-scan" && job.Status == "completed").OrderByDescending(job => job.CompletedAt).Select(job => job.CompletedAt).FirstOrDefaultAsync(cancellationToken),
            await entries.CountAsync(entry => entry.MangaDexId != "" && (entry.MangaDexLastSyncedAt == null || entry.MangaDexLastSyncedAt < now.AddHours(-30)), cancellationToken),
            await entries.CountAsync(entry => entry.MangaUpdatesId != "" && (entry.MangaUpdatesLastSyncedAt == null || entry.MangaUpdatesLastSyncedAt < now.AddHours(-30)), cancellationToken),
            recentJobs);
    }

    public async Task<MaintenanceJobResponse?> QueueAsync(Guid requestedByUserId, string type, CancellationToken cancellationToken)
    {
        return await QueueAsync(requestedByUserId, type, "manual", cancellationToken);
    }

    public Task<List<MaintenanceJobResponse>> ListHistoryAsync(int offset, int limit, CancellationToken cancellationToken) =>
        db.MaintenanceJobs.AsNoTracking()
            .OrderByDescending(job => job.RequestedAt)
            .Skip(Math.Max(0, offset))
            .Take(Math.Clamp(limit, 1, 100))
            .Select(job => new MaintenanceJobResponse(job.Id, job.Type, job.Trigger, job.Status, job.RequestedAt, job.StartedAt, job.CompletedAt, job.Error))
            .ToListAsync(cancellationToken);

    public Task<MaintenanceJobResponse?> QueueAutomaticAsync(string type, string trigger, CancellationToken cancellationToken) =>
        QueueAsync(Guid.Empty, type, trigger, cancellationToken);

    private async Task<MaintenanceJobResponse?> QueueAsync(Guid requestedByUserId, string type, string trigger, CancellationToken cancellationToken)
    {
        var normalized = type.Trim().ToLowerInvariant();
        if (!AllowedJobTypes.Contains(normalized)) return null;
        var existing = await db.MaintenanceJobs.FirstOrDefaultAsync(job => job.Type == normalized && (job.Status == "queued" || job.Status == "running"), cancellationToken);
        if (existing is not null) return ToResponse(existing);
        var job = new MaintenanceJob { Type = normalized, Trigger = trigger, RequestedByUserId = requestedByUserId };
        db.MaintenanceJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(job);
    }

    private static MaintenanceJobResponse ToResponse(MaintenanceJob job) => new(job.Id, job.Type, job.Trigger, job.Status, job.RequestedAt, job.StartedAt, job.CompletedAt, job.Error);

    private static CacheUsage GetCacheUsage(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return new CacheUsage();

            var archiveRoot = Path.GetFullPath(Path.Combine(root, "archive")) + Path.DirectorySeparatorChar;
            var files = Directory.EnumerateFiles(root, "*.cbz", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path));
            var usage = new CacheUsage();

            foreach (var file in files)
            {
                if (Path.GetFullPath(file.FullName).StartsWith(archiveRoot, StringComparison.OrdinalIgnoreCase))
                {
                    usage.ArchivedChapters++;
                    usage.ArchivedBytes += file.Length;
                }
                else
                {
                    usage.ActiveChapters++;
                    usage.ActiveBytes += file.Length;
                }
            }

            return usage;
        }
        catch (IOException) { return new CacheUsage(); }
        catch (UnauthorizedAccessException) { return new CacheUsage(); }
    }

    private sealed class CacheUsage
    {
        public int ActiveChapters { get; set; }
        public long ActiveBytes { get; set; }
        public int ArchivedChapters { get; set; }
        public long ArchivedBytes { get; set; }
        public int TotalChapters => ActiveChapters + ArchivedChapters;
        public long TotalBytes => ActiveBytes + ArchivedBytes;
    }
}
