using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Services;

public sealed class AdminOperationsService(MangaHubDbContext db, IOptions<MangaHubOptions> options)
{
    private static readonly HashSet<string> AllowedJobTypes = ["release-sync", "mangadex-status-sync", "prefetch", "mangadex-cache-cleanup", "mangadex-archive-integrity-check", "mangaupdates-sync", CatalogIdentityEnrichmentService.JobType, "library-scan", "idle-backfill"];

    public async Task<OperationsOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = db.MangaEntries.AsNoTracking();
        var recentJobs = await db.MaintenanceJobs.AsNoTracking().OrderByDescending(job => job.RequestedAt).Take(12)
            .Select(job => new MaintenanceJobResponse(job.Id, job.Type, job.Trigger, job.Status, job.RequestedAt, job.StartedAt, job.CompletedAt, job.Error)).ToListAsync(cancellationToken);
        var cacheRoot = options.Value.MangaDexCachePath;
        var cacheUsage = GetCacheUsage(cacheRoot);
        var reclaimableUsage = await GetReclaimableCacheUsageAsync(cancellationToken);
        var telemetryCutoff = now.AddDays(-30);
        var archiveRestores = await db.ArchiveRecoveryEvents.CountAsync(item => item.Action == "restored" && item.OccurredAt >= telemetryCutoff, cancellationToken);
        var archiveRedownloads = await db.ArchiveRecoveryEvents.CountAsync(item => item.Action == "redownloaded" && item.OccurredAt >= telemetryCutoff, cancellationToken);
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
            recentJobs,
            cacheUsage.OriginalChapters,
            cacheUsage.OriginalBytes,
            cacheUsage.DataSaverChapters,
            cacheUsage.DataSaverBytes,
            reclaimableUsage.Chapters,
            reclaimableUsage.Bytes,
            archiveRestores,
            archiveRedownloads);
    }

    public async Task<MaintenanceJobResponse?> QueueAsync(Guid requestedByUserId, string type, CancellationToken cancellationToken)
    {
        return await QueueAsync(requestedByUserId, type, "manual", cancellationToken);
    }

    public Task<List<MaintenanceJobResponse>> ListHistoryAsync(int offset, int limit, string? type, string? status, string? trigger, CancellationToken cancellationToken)
    {
        var query = db.MaintenanceJobs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(job => job.Type == type.Trim().ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(job => job.Status == status.Trim().ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(trigger)) query = query.Where(job => job.Trigger == trigger.Trim().ToLowerInvariant());

        return query
            .OrderByDescending(job => job.RequestedAt)
            .Skip(Math.Max(0, offset))
            .Take(Math.Clamp(limit, 1, 100))
            .Select(job => new MaintenanceJobResponse(job.Id, job.Type, job.Trigger, job.Status, job.RequestedAt, job.StartedAt, job.CompletedAt, job.Error))
            .ToListAsync(cancellationToken);
    }

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
                var fullPath = Path.GetFullPath(file.FullName);
                var isArchive = fullPath.StartsWith(archiveRoot, StringComparison.OrdinalIgnoreCase);
                var isDataSaver = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(segment => string.Equals(segment, "data-saver", StringComparison.OrdinalIgnoreCase));
                if (isArchive)
                {
                    usage.ArchivedChapters++;
                    usage.ArchivedBytes += file.Length;
                }
                else
                {
                    usage.ActiveChapters++;
                    usage.ActiveBytes += file.Length;
                }

                if (isDataSaver)
                {
                    usage.DataSaverChapters++;
                    usage.DataSaverBytes += file.Length;
                }
                else
                {
                    usage.OriginalChapters++;
                    usage.OriginalBytes += file.Length;
                }
            }

            return usage;
        }
        catch (IOException) { return new CacheUsage(); }
        catch (UnauthorizedAccessException) { return new CacheUsage(); }
    }

    private async Task<ReclaimableCacheUsage> GetReclaimableCacheUsageAsync(CancellationToken cancellationToken)
    {
        var activeProgress = await db.UserMangaEntries
            .Where(shelf => (shelf.ReadingStatus == "reading" || shelf.ReadingStatus == "paused")
                && shelf.MangaEntry!.MangaDexId != "")
            .Select(shelf => new { shelf.MangaEntry!.MangaDexId, shelf.CurrentChapter })
            .ToListAsync(cancellationToken);
        var earliestActiveChapterByMangaDexId = activeProgress
            .GroupBy(item => item.MangaDexId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { group.Key, Earliest = MangaDexCacheRetentionPolicy.FindEarliestRecordedChapter(group.Select(item => item.CurrentChapter)) })
            .Where(group => group.Earliest is not null)
            .ToDictionary(group => group.Key, group => group.Earliest!.Value, StringComparer.OrdinalIgnoreCase);
        var series = await db.Series.AsNoTracking().Include(item => item.Chapters)
            .Where(item => item.Source == "mangadex-cache")
            .ToListAsync(cancellationToken);
        var root = Path.GetFullPath(options.Value.MangaDexCachePath);
        var result = new ReclaimableCacheUsage();

        foreach (var cached in series)
        {
            earliestActiveChapterByMangaDexId.TryGetValue(cached.ExternalId, out var earliest);
            var retainFrom = earliestActiveChapterByMangaDexId.ContainsKey(cached.ExternalId) ? earliest : (decimal?)null;
            foreach (var group in cached.Chapters.GroupBy(chapter => chapter.SourceId, StringComparer.Ordinal))
            {
                var original = group.FirstOrDefault(chapter => !string.Equals(chapter.ImageQuality, "data-saver", StringComparison.OrdinalIgnoreCase));
                if (original is null) continue;
                var lastAccessedAt = group.Max(chapter => chapter.LastAccessedAt ?? chapter.CreatedAt);
                if (MangaDexCacheRetentionPolicy.ShouldRetain(original.SourceId, original.ChapterNumber, retainFrom, lastAccessedAt, DateTimeOffset.UtcNow, options.Value.MangaDexCacheRetentionGraceDays)) continue;

                var path = Path.GetFullPath(Path.Combine(root, "mangadex", cached.ExternalId, $"{original.SourceId}.cbz"));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                result.Chapters++;
                result.Bytes += new FileInfo(path).Length;
            }
        }

        return result;
    }

    private sealed class CacheUsage
    {
        public int ActiveChapters { get; set; }
        public long ActiveBytes { get; set; }
        public int ArchivedChapters { get; set; }
        public long ArchivedBytes { get; set; }
        public int OriginalChapters { get; set; }
        public long OriginalBytes { get; set; }
        public int DataSaverChapters { get; set; }
        public long DataSaverBytes { get; set; }
        public int TotalChapters => ActiveChapters + ArchivedChapters;
        public long TotalBytes => ActiveBytes + ArchivedBytes;
    }

    private sealed class ReclaimableCacheUsage
    {
        public int Chapters { get; set; }
        public long Bytes { get; set; }
    }
}
