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

    public async Task<ArchiveOverviewResponse> GetArchiveOverviewAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var graceDays = Math.Clamp(options.Value.MangaDexCacheRetentionGraceDays, 0, 90);
        var root = Path.GetFullPath(options.Value.MangaDexCachePath);
        var usage = GetCacheUsage(root);
        var entries = await db.MangaEntries.AsNoTracking()
            .Where(entry => entry.MangaDexId != "")
            .Select(entry => new { entry.MangaDexId, entry.Title })
            .ToListAsync(cancellationToken);
        var titles = entries
            .GroupBy(entry => entry.MangaDexId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Title, StringComparer.OrdinalIgnoreCase);
        var readerProgressByMangaDexId = await GetReaderProgressByMangaDexIdAsync(cancellationToken);
        var series = await db.Series.AsNoTracking()
            .Include(item => item.Chapters)
            .Where(item => item.Source == "mangadex-cache")
            .ToListAsync(cancellationToken);

        var activeFiles = GetCacheFiles(root, archived: false)
            .GroupBy(file => CacheKey(file.MangaDexId, file.SourceId), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var archivedFiles = GetCacheFiles(root, archived: true)
            .GroupBy(file => CacheKey(file.MangaDexId, file.SourceId), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var manga = new Dictionary<string, ArchiveMangaRetention>(StringComparer.OrdinalIgnoreCase);
        var readingManga = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var graceManga = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var readyManga = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var readingChapters = 0;
        var graceChapters = 0;
        var readyChapters = 0;
        var readingBytes = 0L;
        var graceBytes = 0L;
        var readyBytes = 0L;
        var unmanagedChapters = 0;
        var unmanagedBytes = 0L;

        foreach (var cached in series)
        {
            readerProgressByMangaDexId.TryGetValue(cached.ExternalId, out var readerProgress);
            readerProgress ??= [];
            var view = GetManga(manga, cached.ExternalId, titles.GetValueOrDefault(cached.ExternalId, cached.Title));
            var mostProtective = MangaDexCacheRetentionPolicy.FindMostProtectiveProgress(readerProgress);
            view.ReaderProtectionDetail = mostProtective is null
                ? null
                : $"{(mostProtective.IncludeCurrentChapter ? "from" : "after")} Ch. {mostProtective.CurrentChapter}";

            foreach (var chapterGroup in cached.Chapters.GroupBy(chapter => chapter.SourceId, StringComparer.Ordinal))
            {
                var key = CacheKey(cached.ExternalId, chapterGroup.Key);
                if (activeFiles.Remove(key, out var active))
                {
                    var chapter = chapterGroup.First();
                    var bytes = active.Sum(file => file.Bytes);
                    view.ActiveChapterCount += active.Count;
                    view.ActiveBytes += bytes;
                    var lastAccessedAt = chapterGroup.Select(item => item.LastReaderOpenedAt).Max();
                    var protectedForReader = MangaDexCacheRetentionPolicy.IsProtectedForReader(chapter.ChapterNumber, readerProgress);
                    var protectedByGrace = lastAccessedAt >= now.AddDays(-graceDays);

                    if (protectedForReader)
                    {
                        readingManga.Add(cached.ExternalId);
                        readingChapters += active.Count;
                        readingBytes += bytes;
                        view.ReadingProtectedChapterCount += active.Count;
                    }
                    else if (protectedByGrace)
                    {
                        graceManga.Add(cached.ExternalId);
                        graceChapters += active.Count;
                        graceBytes += bytes;
                        view.GracePeriodChapterCount += active.Count;
                    }
                    else if (!MangaDexCacheRetentionPolicy.ShouldRetain(chapter.SourceId, chapter.ChapterNumber, readerProgress, lastAccessedAt, now, graceDays))
                    {
                        readyManga.Add(cached.ExternalId);
                        readyChapters += active.Count;
                        readyBytes += bytes;
                        view.ReadyToArchiveChapterCount += active.Count;
                    }
                    else
                    {
                        view.UnmanagedActiveChapterCount += active.Count;
                        unmanagedChapters += active.Count;
                        unmanagedBytes += bytes;
                    }
                }

                if (archivedFiles.Remove(key, out var archived))
                {
                    view.ArchivedChapterCount += archived.Count;
                    view.ArchivedBytes += archived.Sum(file => file.Bytes);
                }
            }
        }

        foreach (var fileGroup in activeFiles.Values)
        {
            var sample = fileGroup[0];
            var view = GetManga(manga, sample.MangaDexId, titles.GetValueOrDefault(sample.MangaDexId, "Unindexed cache files"));
            view.ActiveChapterCount += fileGroup.Count;
            view.ActiveBytes += fileGroup.Sum(file => file.Bytes);
            view.UnmanagedActiveChapterCount += fileGroup.Count;
            unmanagedChapters += fileGroup.Count;
            unmanagedBytes += fileGroup.Sum(file => file.Bytes);
        }
        foreach (var fileGroup in archivedFiles.Values)
        {
            var sample = fileGroup[0];
            var view = GetManga(manga, sample.MangaDexId, titles.GetValueOrDefault(sample.MangaDexId, "Unindexed cache files"));
            view.ArchivedChapterCount += fileGroup.Count;
            view.ArchivedBytes += fileGroup.Sum(file => file.Bytes);
        }

        var telemetryCutoff = now.AddDays(-30);
        var restores = await db.ArchiveRecoveryEvents.CountAsync(item => item.Action == "restored" && item.OccurredAt >= telemetryCutoff, cancellationToken);
        var redownloads = await db.ArchiveRecoveryEvents.CountAsync(item => item.Action == "redownloaded" && item.OccurredAt >= telemetryCutoff, cancellationToken);
        return new ArchiveOverviewResponse(
            usage.ActiveChapters, usage.ActiveBytes, usage.ArchivedChapters, usage.ArchivedBytes,
            readingManga.Count, readingChapters, readingBytes,
            graceManga.Count, graceChapters, graceBytes,
            readyManga.Count, readyChapters, readyBytes,
            unmanagedChapters, unmanagedBytes,
            graceDays, restores, redownloads,
            manga.Values
                .Where(item => item.ActiveChapterCount > 0 || item.ArchivedChapterCount > 0)
                .OrderByDescending(item => item.ActiveBytes)
                .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.ToResponse())
                .ToList());
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

    private static ArchiveMangaRetention GetManga(Dictionary<string, ArchiveMangaRetention> manga, string mangaDexId, string title)
    {
        if (!manga.TryGetValue(mangaDexId, out var result))
        {
            result = new ArchiveMangaRetention(mangaDexId, title);
            manga[mangaDexId] = result;
        }
        return result;
    }

    private static List<CacheFile> GetCacheFiles(string root, bool archived)
    {
        try
        {
            var baseDirectory = archived
                ? Path.Combine(root, "archive", "mangadex", "data-saver")
                : Path.Combine(root, "mangadex");
            if (!Directory.Exists(baseDirectory)) return [];

            return Directory.EnumerateFiles(baseDirectory, "*.cbz", SearchOption.AllDirectories)
                .Select(path => ToCacheFile(baseDirectory, path, archived))
                .Where(file => file is not null)
                .Cast<CacheFile>()
                .ToList();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static CacheFile? ToCacheFile(string baseDirectory, string path, bool archived)
    {
        var parts = Path.GetRelativePath(baseDirectory, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var isDataSaver = archived || (parts.Length >= 3 && string.Equals(parts[0], "data-saver", StringComparison.OrdinalIgnoreCase));
        var offset = isDataSaver && !archived ? 1 : 0;
        if (parts.Length - offset != 2) return null;
        var info = new FileInfo(path);
        return new CacheFile(parts[offset], Path.GetFileNameWithoutExtension(parts[offset + 1]), info.Length);
    }

    private static string CacheKey(string mangaDexId, string sourceId) => $"{mangaDexId}\u001f{sourceId}";

    private async Task<ReclaimableCacheUsage> GetReclaimableCacheUsageAsync(CancellationToken cancellationToken)
    {
        var readerProgressByMangaDexId = await GetReaderProgressByMangaDexIdAsync(cancellationToken);
        var series = await db.Series.AsNoTracking().Include(item => item.Chapters)
            .Where(item => item.Source == "mangadex-cache")
            .ToListAsync(cancellationToken);
        var root = Path.GetFullPath(options.Value.MangaDexCachePath);
        var result = new ReclaimableCacheUsage();

        foreach (var cached in series)
        {
            readerProgressByMangaDexId.TryGetValue(cached.ExternalId, out var readerProgress);
            readerProgress ??= [];
            foreach (var group in cached.Chapters.GroupBy(chapter => chapter.SourceId, StringComparer.Ordinal))
            {
                var original = group.FirstOrDefault(chapter => !string.Equals(chapter.ImageQuality, "data-saver", StringComparison.OrdinalIgnoreCase));
                if (original is null) continue;
                var lastAccessedAt = group.Select(chapter => chapter.LastReaderOpenedAt).Max();
                if (MangaDexCacheRetentionPolicy.ShouldRetain(original.SourceId, original.ChapterNumber, readerProgress, lastAccessedAt, DateTimeOffset.UtcNow, options.Value.MangaDexCacheRetentionGraceDays)) continue;

                var path = Path.GetFullPath(Path.Combine(root, "mangadex", cached.ExternalId, $"{original.SourceId}.cbz"));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                result.Chapters++;
                result.Bytes += new FileInfo(path).Length;
            }
        }

        return result;
    }

    private async Task<Dictionary<string, IReadOnlyCollection<MangaDexCacheRetentionPolicy.ReaderProgress>>> GetReaderProgressByMangaDexIdAsync(CancellationToken cancellationToken)
    {
        var progress = await db.UserMangaEntries.AsNoTracking()
            .Where(shelf => shelf.ReadingStatus == "reading" && shelf.MangaEntry!.MangaDexId != "")
            .Select(shelf => new { shelf.MangaEntry!.MangaDexId, shelf.CurrentChapter, shelf.IsRead })
            .ToListAsync(cancellationToken);

        return progress
            .GroupBy(item => item.MangaDexId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<MangaDexCacheRetentionPolicy.ReaderProgress>)group
                    .Select(item => new MangaDexCacheRetentionPolicy.ReaderProgress(item.CurrentChapter, !item.IsRead))
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
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

    private sealed record CacheFile(string MangaDexId, string SourceId, long Bytes);

    private sealed class ArchiveMangaRetention(string mangaDexId, string title)
    {
        public string MangaDexId { get; } = mangaDexId;
        public string Title { get; } = title;
        public int ActiveChapterCount { get; set; }
        public long ActiveBytes { get; set; }
        public int ReadingProtectedChapterCount { get; set; }
        public int GracePeriodChapterCount { get; set; }
        public int ReadyToArchiveChapterCount { get; set; }
        public int UnmanagedActiveChapterCount { get; set; }
        public string? ReaderProtectionDetail { get; set; }
        public int ArchivedChapterCount { get; set; }
        public long ArchivedBytes { get; set; }

        public ArchiveMangaRetentionResponse ToResponse() => new(
            MangaDexId, Title, ActiveChapterCount, ActiveBytes,
            ReadingProtectedChapterCount, GracePeriodChapterCount, ReadyToArchiveChapterCount,
            UnmanagedActiveChapterCount,
            ReaderProtectionDetail, ArchivedChapterCount, ArchivedBytes);
    }
}
