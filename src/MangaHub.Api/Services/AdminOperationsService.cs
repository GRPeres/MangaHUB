using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Services;

public sealed class AdminOperationsService(MangaHubDbContext db, IOptions<MangaHubOptions> options)
{
    private static readonly HashSet<string> AllowedJobTypes = ["release-sync", "mangaupdates-sync", "mangaupdates-match", "library-scan"];

    public async Task<OperationsOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = db.MangaEntries.AsNoTracking();
        var recentJobs = await db.MaintenanceJobs.AsNoTracking().OrderByDescending(job => job.RequestedAt).Take(12)
            .Select(job => new MaintenanceJobResponse(job.Id, job.Type, job.Status, job.RequestedAt, job.StartedAt, job.CompletedAt, job.Error)).ToListAsync(cancellationToken);
        var cacheRoot = options.Value.MangaDexCachePath;
        var (cachedChapters, cacheBytes) = GetCacheUsage(cacheRoot);
        return new OperationsOverviewResponse(
            await entries.CountAsync(cancellationToken),
            await entries.CountAsync(entry => entry.MangaDexId != "", cancellationToken),
            await entries.CountAsync(entry => entry.MangaUpdatesId != "", cancellationToken),
            cachedChapters,
            cacheBytes,
            await entries.MaxAsync(entry => entry.MangaDexLastSyncedAt, cancellationToken),
            await entries.MaxAsync(entry => entry.MangaUpdatesLastSyncedAt, cancellationToken),
            await db.MaintenanceJobs.AsNoTracking().Where(job => job.Type == "library-scan" && job.Status == "completed").OrderByDescending(job => job.CompletedAt).Select(job => job.CompletedAt).FirstOrDefaultAsync(cancellationToken),
            await entries.CountAsync(entry => entry.MangaDexId != "" && (entry.MangaDexLastSyncedAt == null || entry.MangaDexLastSyncedAt < now.AddHours(-30)), cancellationToken),
            await entries.CountAsync(entry => entry.MangaUpdatesId != "" && (entry.MangaUpdatesLastSyncedAt == null || entry.MangaUpdatesLastSyncedAt < now.AddHours(-30)), cancellationToken),
            recentJobs);
    }

    public async Task<MaintenanceJobResponse?> QueueAsync(Guid requestedByUserId, string type, CancellationToken cancellationToken)
    {
        var normalized = type.Trim().ToLowerInvariant();
        if (!AllowedJobTypes.Contains(normalized)) return null;
        var existing = await db.MaintenanceJobs.FirstOrDefaultAsync(job => job.Type == normalized && (job.Status == "queued" || job.Status == "running"), cancellationToken);
        if (existing is not null) return ToResponse(existing);
        var job = new MaintenanceJob { Type = normalized, RequestedByUserId = requestedByUserId };
        db.MaintenanceJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(job);
    }

    private static MaintenanceJobResponse ToResponse(MaintenanceJob job) => new(job.Id, job.Type, job.Status, job.RequestedAt, job.StartedAt, job.CompletedAt, job.Error);

    private static (int Chapters, long Bytes) GetCacheUsage(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return (0, 0);
            var files = Directory.EnumerateFiles(root, "*.cbz", SearchOption.AllDirectories).Select(path => new FileInfo(path)).ToList();
            return (files.Count, files.Sum(file => file.Length));
        }
        catch (IOException) { return (0, 0); }
        catch (UnauthorizedAccessException) { return (0, 0); }
    }
}
