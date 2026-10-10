using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using MangaHub.Infrastructure.RemoteJobs;
using MangaHub.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebPush;

namespace MangaHub.Api.Services;

/// <summary>
/// Executes remote maintenance in the API process so every provider request is
/// governed by this process's shared provider scheduler.
/// </summary>
public sealed class RemoteMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<MangaHubOptions> options,
    RemoteJobPriorityContext priorityContext,
    ILogger<RemoteMaintenanceService> logger)
{
    private const string MangaDexCacheSource = "mangadex-cache";
    public async Task<MaintenanceRunResult> RunRequestedAsync(string type, CancellationToken cancellationToken)
    {
        var priority = type switch
        {
            "release-sync" or "mangadex-status-sync" or "mangadex-language-coverage-check" or "mangaupdates-sync" => RemoteJobPriority.ReleaseSync,
            "prefetch" => RemoteJobPriority.Prefetch,
            "mangadex-cache-cleanup" or "mangadex-archive-integrity-check" or "mangaupdates-match" or CatalogIdentityEnrichmentService.JobType => RemoteJobPriority.Maintenance,
            "idle-backfill" => RemoteJobPriority.Backfill,
            _ => throw new InvalidOperationException($"Unsupported remote maintenance job '{type}'.")
        };

        using var priorityScope = priorityContext.Push(priority);
        var shouldContinue = false;
        switch (type)
        {
            case "release-sync": await RunReleaseSyncAsync(cancellationToken); break;
            case "mangadex-status-sync": await RunMangaDexStatusSyncAsync(cancellationToken); break;
            case "mangadex-language-coverage-check": await RunMangaDexTranslationCoverageCheckAsync(cancellationToken); break;
            case "prefetch": await RunPrefetchAsync(cancellationToken); break;
            case "mangadex-cache-cleanup": shouldContinue = await RunCacheRetentionAsync(cancellationToken); break;
            case "mangadex-archive-integrity-check": await RunArchiveIntegrityCheckAsync(cancellationToken); break;
            case "mangaupdates-sync": await RunMangaUpdatesSyncAsync(cancellationToken); break;
            case "mangaupdates-match": await RunMangaUpdatesMatchingAsync(cancellationToken); break;
            case CatalogIdentityEnrichmentService.JobType: await RunCatalogIdentityEnrichmentAsync(cancellationToken); break;
            case "idle-backfill": await RunIdleBackfillAsync(cancellationToken); break;
        }

        return new MaintenanceRunResult(shouldContinue);
    }

    private async Task RunCatalogIdentityEnrichmentAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var enrichment = scope.ServiceProvider.GetRequiredService<CatalogIdentityEnrichmentService>();
            await enrichment.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Catalog identity enrichment run failed.");
        }
    }

    private async Task RunReleaseSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SyncMangaDexCatalogAsync(false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MangaDex release sync run failed. Due entries will retry on the next poll.");
        }
    }

    private async Task RunMangaDexStatusSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SyncMangaDexCatalogAsync(true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MangaDex status sync run failed.");
        }
    }

    private async Task RunMangaDexTranslationCoverageCheckAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MangaDexEnabled)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var coverage = scope.ServiceProvider.GetRequiredService<MangaDexTranslationCoverageService>();
            await coverage.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MangaDex translation coverage check failed.");
            throw;
        }
    }

    private async Task RunPrefetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            await PrefetchNewMangaDexChaptersAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MangaDex chapter pre-download maintenance failed.");
        }
    }

    private async Task RunMangaUpdatesMatchingAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MangaUpdatesEnabled)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
            var matcher = scope.ServiceProvider.GetRequiredService<MangaUpdatesCatalogMatchService>();
            var retryCutoff = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(options.Value.MangaUpdatesMatchRetryHours, 6, 24 * 30));
            var batchSize = Math.Clamp(options.Value.MangaUpdatesMatchBatchSize, 1, 50);
            var checkedCount = 0;
            var matchedCount = 0;

            while (true)
            {
                var entries = await db.MangaEntries
                    .Where(entry => entry.MangaUpdatesId == "" &&
                        (entry.MangaUpdatesLastMatchAttemptAt == null || entry.MangaUpdatesLastMatchAttemptAt < retryCutoff))
                    .OrderBy(entry => entry.MangaUpdatesLastMatchAttemptAt ?? DateTimeOffset.MinValue)
                    .ThenBy(entry => entry.Title)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);
                if (entries.Count == 0)
                {
                    break;
                }

                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var match = await matcher.FindAsync(entry.Title, entry.MediaType, entry.FirstPublishYear, cancellationToken);
                        entry.MangaUpdatesLastMatchAttemptAt = DateTimeOffset.UtcNow;
                        if (match is not null)
                        {
                            entry.MangaUpdatesId = match.Id;
                            entry.UpdatedAt = DateTimeOffset.UtcNow;
                            matchedCount++;
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
                    {
                        entry.MangaUpdatesLastMatchAttemptAt = DateTimeOffset.UtcNow;
                        logger.LogWarning(ex, "MangaUpdates matching failed for {Title}.", entry.Title);
                    }

                    await db.SaveChangesAsync(cancellationToken);
                    checkedCount++;
                }

                if (entries.Count < batchSize)
                {
                    break;
                }
            }

            logger.LogInformation("MangaUpdates identity repair checked {CheckedCount} unbound entries and matched {MatchedCount}.", checkedCount, matchedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MangaUpdates identity repair run failed.");
        }
    }

    private async Task RunMangaUpdatesSyncAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MangaUpdatesEnabled)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
            var client = scope.ServiceProvider.GetRequiredService<IMangaUpdatesClient>();
            var cutoff = DateTimeOffset.UtcNow.AddHours(-Math.Clamp(options.Value.MangaUpdatesSyncIntervalHours, 1, 24 * 30));
            var entries = await db.MangaEntries
                .Where(entry => entry.MangaUpdatesId != "" &&
                    (entry.MangaUpdatesLastSyncedAt == null || entry.MangaUpdatesLastSyncedAt < cutoff))
                .OrderBy(entry => entry.MangaUpdatesLastSyncedAt ?? DateTimeOffset.MinValue)
                .ThenBy(entry => entry.Title)
                .Take(Math.Clamp(options.Value.MangaUpdatesSyncBatchSize, 1, 100))
                .ToListAsync(cancellationToken);
            var updated = 0;

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var details = await client.GetSeriesAsync(entry.MangaUpdatesId, cancellationToken);
                    if (details is null)
                    {
                        continue;
                    }

                    var changed = entry.MangaUpdatesLatestChapter != details.LatestChapter
                        || entry.MangaUpdatesStatus != details.Status
                        || entry.MangaUpdatesCompleted != details.Completed;
                    entry.MangaUpdatesLatestChapter = details.LatestChapter;
                    entry.MangaUpdatesStatus = details.Status;
                    entry.MangaUpdatesCompleted = details.Completed;
                    entry.MangaUpdatesLastSyncedAt = DateTimeOffset.UtcNow;
                    if (changed)
                    {
                        entry.UpdatedAt = DateTimeOffset.UtcNow;
                        updated++;
                    }

                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
                {
                    logger.LogWarning(ex, "MangaUpdates sync failed for {Title} ({MangaUpdatesId}).", entry.Title, entry.MangaUpdatesId);
                }

            }

            logger.LogInformation("MangaUpdates source sync checked {CheckedCount} entries and updated {UpdatedCount}.", entries.Count, updated);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MangaUpdates source sync run failed.");
        }
    }

    private async Task SyncMangaDexCatalogAsync(bool force, CancellationToken cancellationToken)
    {
        if (!options.Value.MangaDexEnabled)
        {
            logger.LogInformation("MangaDex catalog sync skipped because MangaDex is disabled.");
            return;
        }

        var syncInterval = TimeSpan.FromHours(Math.Max(1, options.Value.MangaDexSyncIntervalHours));
        var cutoff = DateTimeOffset.UtcNow.Subtract(syncInterval);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
        var totalLinkedEntries = await db.MangaEntries.CountAsync(entry => entry.MangaDexId != "", cancellationToken);
        var batchSize = force
            ? Math.Clamp(options.Value.MangaDexSyncMaxBatchSize, 1, 1000)
            : GetReleaseSyncBatchSize(totalLinkedEntries);

        var entries = await db.MangaEntries
            .Where(entry => entry.MangaDexId != "" &&
                (force
                    || entry.MangaDexLatestChapter == null
                    || !db.MangaDexLanguageLatestChapters.Any(latest => latest.MangaEntryId == entry.Id)
                    || entry.MangaDexLastSyncedAt == null
                    || entry.MangaDexLastSyncedAt < cutoff))
            .OrderByDescending(entry => !db.MangaDexLanguageLatestChapters.Any(latest => latest.MangaEntryId == entry.Id))
            .ThenByDescending(entry => entry.MangaDexLatestChapter == null)
            .ThenBy(entry => entry.MangaDexLastSyncedAt ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.Title)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
        {
            logger.LogInformation("MangaDex catalog sync found no stale catalog entries.");
            return;
        }

        var client = httpClientFactory.CreateClient("mangadex-sync");
        var mangaDex = scope.ServiceProvider.GetRequiredService<MangaSourceRegistry>().Get("mangadex");
        var updated = 0;
        var resumed = 0;
        var paused = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var sourceSeries = await mangaDex.GetSeriesAsync(entry.MangaDexId, cancellationToken);
                if (sourceSeries is not null && !string.IsNullOrWhiteSpace(sourceSeries.Status))
                {
                    var statusChanged = !string.Equals(entry.PublishingStatus, sourceSeries.Status, StringComparison.OrdinalIgnoreCase);
                    entry.PublishingStatus = sourceSeries.Status;

                    if (IsMangaDexHiatus(sourceSeries.Status))
                    {
                        var activeShelfEntries = await db.UserMangaEntries
                            .Where(shelf => shelf.MangaEntryId == entry.Id
                                && (shelf.ReadingStatus == "reading" || shelf.ReadingStatus == "done"))
                            .ToListAsync(cancellationToken);
                        foreach (var shelfEntry in activeShelfEntries)
                        {
                            shelfEntry.ReadingStatus = "paused";
                            shelfEntry.UpdatedAt = DateTimeOffset.UtcNow;
                            paused++;
                        }
                    }
                    else if (IsMangaDexOngoing(sourceSeries.Status))
                    {
                        var resumableShelfEntries = await db.UserMangaEntries
                            .Where(shelf => shelf.MangaEntryId == entry.Id
                                && (shelf.ReadingStatus == "paused" || shelf.ReadingStatus == "done"))
                            .ToListAsync(cancellationToken);
                        foreach (var shelfEntry in resumableShelfEntries)
                        {
                            shelfEntry.ReadingStatus = "reading";
                            shelfEntry.UpdatedAt = DateTimeOffset.UtcNow;
                            resumed++;
                        }
                    }

                    if (statusChanged)
                    {
                        entry.UpdatedAt = DateTimeOffset.UtcNow;
                        updated++;
                    }
                }

                var latestChapters = await GetLatestChapterNumbersByLanguageAsync(client, entry.MangaDexId, cancellationToken);
                decimal? latestChapter = latestChapters.Count == 0 ? null : latestChapters.Values.Max();
                entry.MangaDexLastSyncedAt = DateTimeOffset.UtcNow;

                var cachedLanguages = await db.MangaDexLanguageLatestChapters
                    .Where(latest => latest.MangaEntryId == entry.Id)
                    .ToDictionaryAsync(latest => latest.Language, StringComparer.OrdinalIgnoreCase, cancellationToken);
                var releasedLanguages = new List<(string Language, decimal Chapter)>();
                foreach (var (language, latestChapterForLanguage) in latestChapters)
                {
                    if (cachedLanguages.TryGetValue(language, out var cached))
                    {
                        if (latestChapterForLanguage > cached.LatestChapter)
                        {
                            releasedLanguages.Add((language, latestChapterForLanguage));
                        }
                        cached.LatestChapter = latestChapterForLanguage;
                        cached.SyncedAt = DateTimeOffset.UtcNow;
                    }
                    else
                    {
                        db.MangaDexLanguageLatestChapters.Add(new MangaDexLanguageLatestChapter
                        {
                            MangaEntryId = entry.Id,
                            Language = language,
                            LatestChapter = latestChapterForLanguage,
                            SyncedAt = DateTimeOffset.UtcNow
                        });
                    }
                }

                // This is a full, page-bearing feed scan. Anything no longer present must not
                // continue advertising a release that the reader cannot actually open.
                foreach (var stale in cachedLanguages.Values.Where(cached => !latestChapters.ContainsKey(cached.Language)).ToList())
                {
                    db.MangaDexLanguageLatestChapters.Remove(stale);
                }

                await CreateReleaseNotificationsAsync(db, entry, releasedLanguages, cancellationToken);

                if (latestChapter is not null)
                {
                    var latestWholeChapter = (int)Math.Floor(latestChapter.Value);
                    if (entry.MangaDexLatestChapter != latestChapter || entry.ChapterCount != latestWholeChapter)
                    {
                        entry.MangaDexLatestChapter = latestChapter;
                        entry.ChapterCount = latestWholeChapter;
                        entry.UpdatedAt = DateTimeOffset.UtcNow;
                        updated++;
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
            {
                logger.LogWarning(ex, "MangaDex catalog sync failed for {Title} ({MangaDexId}).", entry.Title, entry.MangaDexId);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "MangaDex catalog sync checked {CheckedCount} entries, updated {UpdatedCount}, resumed {ResumedCount} shelf entries, and marked {PausedCount} shelf entries as paused for a MangaDex hiatus.",
            entries.Count,
            updated,
            resumed,
            paused);
    }

    private async Task<bool> RunCacheRetentionAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MangaDexCacheRetentionEnabled)
        {
            return false;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
            var cache = scope.ServiceProvider.GetRequiredService<IMangaDexChapterCache>();
            var mangaDex = scope.ServiceProvider.GetRequiredService<MangaSourceRegistry>().Get("mangadex");
            var activeProgress = await db.UserMangaEntries
                .Where(shelf => shelf.ReadingStatus == "reading"
                    && shelf.MangaEntry!.MangaDexId != "")
                .Select(shelf => new { shelf.MangaEntry!.MangaDexId, shelf.CurrentChapter, shelf.IsRead })
                .ToListAsync(cancellationToken);

            var readerProgressByMangaDexId = activeProgress
                .GroupBy(item => item.MangaDexId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key,
                    group => (IReadOnlyCollection<MangaDexCacheRetentionPolicy.ReaderProgress>)group
                        .Select(item => new MangaDexCacheRetentionPolicy.ReaderProgress(item.CurrentChapter, !item.IsRead))
                        .ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var cachedSeries = await db.Series
                .Include(series => series.Chapters)
                .Where(series => series.Source == MangaDexCacheSource)
                .ToListAsync(cancellationToken);
            var batchSize = Math.Clamp(options.Value.MangaDexCacheRetentionBatchSize, 1, 100);
            var archived = 0;
            var considered = 0;
            var alreadyArchived = 0;
            var failed = 0;
            var batchLimitReached = false;
            var cacheRoot = Path.GetFullPath(options.Value.MangaDexCachePath);

            foreach (var cached in cachedSeries)
            {
                readerProgressByMangaDexId.TryGetValue(cached.ExternalId, out var readerProgress);
                readerProgress ??= [];
                foreach (var chapterGroup in cached.Chapters.ToList().GroupBy(chapter => chapter.SourceId, StringComparer.Ordinal))
                {
                    if (considered >= batchSize)
                    {
                        batchLimitReached = true;
                        break;
                    }

                    var chapter = chapterGroup.First();
                    // Cache creation and background prefetching are not reader activity. Only an
                    // explicit reader access earns the temporary archival grace period.
                    var lastAccessedAt = chapterGroup.Select(item => item.LastReaderOpenedAt).Max();
                    if (MangaDexCacheRetentionPolicy.ShouldRetain(
                        chapter.SourceId,
                        chapter.ChapterNumber,
                        readerProgress,
                        lastAccessedAt,
                        DateTimeOffset.UtcNow,
                        options.Value.MangaDexCacheRetentionGraceDays))
                    {
                        continue;
                    }

                    var dataSaver = chapterGroup.FirstOrDefault(item => string.Equals(item.ImageQuality, "data-saver", StringComparison.OrdinalIgnoreCase));
                    var archivedDataSaverPath = Path.Combine(cacheRoot, "archive", "mangadex", "data-saver", cached.ExternalId, $"{chapter.SourceId}.cbz");
                    if (dataSaver is not null && File.Exists(archivedDataSaverPath))
                    {
                        // A restart can occur after the Data Saver file moves but before the
                        // active original and its row are removed. Reconcile that half-finished
                        // archival instead of treating it as permanently ineligible.
                        var staleOriginals = chapterGroup
                            .Where(item => !string.Equals(item.ImageQuality, "data-saver", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        foreach (var original in staleOriginals)
                        {
                            await cache.DeleteAsync(cached.ExternalId, original.SourceId, cancellationToken, original.ImageQuality);
                            db.Chapters.Remove(original);
                        }
                        if (staleOriginals.Count > 0)
                        {
                            await db.SaveChangesAsync(cancellationToken);
                            logger.LogInformation("Reconciled {Count} stale active cache variant(s) after archival of {MangaDexId}/{ChapterId}.", staleOriginals.Count, cached.ExternalId, chapter.SourceId);
                        }

                        // The persisted Data Saver row intentionally remains after archival.
                        alreadyArchived++;
                        continue;
                    }

                    considered++;
                    if (dataSaver is null)
                    {
                        MangaDexCachedChapter archive;
                        try
                        {
                            var pages = await mangaDex.GetPagesAsync(chapter.SourceId, cancellationToken, "data-saver");
                            archive = await cache.EnsureCachedAsync(cached.ExternalId, chapter.SourceId, pages, cancellationToken, imageQuality: "data-saver");
                        }
                        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
                        {
                            try
                            {
                                logger.LogWarning(ex, "MangaDex Data Saver was unavailable for {MangaDexId} chapter {ChapterId}; creating a local archive fallback.", cached.ExternalId, chapter.SourceId);
                                archive = await cache.CreateDataSaverFromOriginalAsync(cached.ExternalId, chapter.SourceId, cancellationToken);
                            }
                            catch (Exception fallbackException) when (fallbackException is not OperationCanceledException)
                            {
                                logger.LogWarning(fallbackException, "Could not create a local Data Saver archive fallback for {MangaDexId} chapter {ChapterId}; preserving the active original.", cached.ExternalId, chapter.SourceId);
                                failed++;
                                continue;
                            }
                        }

                        dataSaver = new MangaChapter
                        {
                            SeriesId = cached.Id,
                            SourceId = chapter.SourceId,
                            ChapterNumber = chapter.ChapterNumber,
                            Language = chapter.Language,
                            ImageQuality = "data-saver",
                            Title = chapter.Title,
                            PageCount = archive.PageCount,
                            FileHash = archive.FileHash
                        };
                        db.Chapters.Add(dataSaver);
                        await db.SaveChangesAsync(cancellationToken);
                    }

                    if (!await cache.ArchiveAsync(cached.ExternalId, chapter.SourceId, cancellationToken, "data-saver"))
                    {
                        failed++;
                        continue;
                    }

                    foreach (var original in chapterGroup.Where(item => !string.Equals(item.ImageQuality, "data-saver", StringComparison.OrdinalIgnoreCase)))
                    {
                        await cache.DeleteAsync(cached.ExternalId, original.SourceId, cancellationToken, original.ImageQuality);
                        db.Chapters.Remove(original);
                    }
                    await db.SaveChangesAsync(cancellationToken);
                    archived++;
                }

                if (batchLimitReached)
                {
                    break;
                }
            }

            logger.LogInformation(
                "MangaDex cache retention archived {ArchivedCount} of {ConsideredCount} considered chapters, skipped {AlreadyArchivedCount} already archived chapters, and could not archive {FailedCount} chapters (batch limit {BatchSize}, more work pending: {BatchLimitReached}); only unfinished current chapters and chapters beyond completed Reading progress are reader-protected.",
                archived,
                considered,
                alreadyArchived,
                failed,
                batchSize,
                batchLimitReached);
            return MangaDexCacheRetentionPolicy.ShouldQueueContinuation(batchLimitReached, archived);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MangaDex cache retention could not finish; it will retry at the next maintenance run.");
            return false;
        }
    }

    private async Task RunArchiveIntegrityCheckAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
        var sampleSize = Math.Clamp(options.Value.MangaDexArchiveIntegritySampleSize, 1, 50);
        var chapters = await db.Chapters
            .Include(chapter => chapter.Series)
            .Where(chapter => chapter.Series!.Source == MangaDexCacheSource
                && string.Equals(chapter.ImageQuality, "data-saver", StringComparison.OrdinalIgnoreCase))
            .OrderBy(chapter => chapter.Id)
            .ToListAsync(cancellationToken);
        if (chapters.Count == 0)
        {
            logger.LogInformation("MangaDex archive integrity check skipped because no Data Saver chapters are recorded.");
            return;
        }

        var offset = (DateTimeOffset.UtcNow.DayOfYear * sampleSize) % chapters.Count;
        var candidates = chapters.Skip(offset).Concat(chapters.Take(offset));
        var verified = 0;
        var corrupt = 0;
        var root = Path.GetFullPath(options.Value.MangaDexCachePath);

        foreach (var chapter in candidates)
        {
            if (verified >= sampleSize) break;
            cancellationToken.ThrowIfCancellationRequested();
            var series = chapter.Series;
            if (series is null) continue;

            var path = Path.GetFullPath(Path.Combine(root, "archive", "mangadex", "data-saver", series.ExternalId, $"{chapter.SourceId}.cbz"));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                // This chapter may currently be restored to active cache; it is not an archive failure.
                continue;
            }

            verified++;
            try
            {
                await ValidateArchiveAsync(path, chapter.PageCount, cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                corrupt++;
                await OpenArchiveIntegrityIssueAsync(db, series, chapter, ex.Message, cancellationToken);
                logger.LogError(ex, "Archived MangaDex chapter failed integrity validation: {MangaDexId}/{ChapterId}.", series.ExternalId, chapter.SourceId);
            }
        }

        logger.LogInformation("MangaDex archive integrity validated {CheckedCount} Data Saver CBZ files; {CorruptCount} require attention.", verified, corrupt);
    }

    private static async Task ValidateArchiveAsync(string path, int expectedPageCount, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(path);
        var pages = archive.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Name)).ToList();
        if (pages.Count == 0 || pages.Count != expectedPageCount)
        {
            throw new InvalidDataException($"Expected {expectedPageCount} readable pages but found {pages.Count}.");
        }

        foreach (var page in pages)
        {
            await using var stream = page.Open();
            await stream.CopyToAsync(Stream.Null, cancellationToken);
        }
    }

    private static async Task OpenArchiveIntegrityIssueAsync(MangaHubDbContext db, MangaSeries series, MangaChapter chapter, string error, CancellationToken cancellationToken)
    {
        var subjectId = new Guid(MD5.HashData(Encoding.UTF8.GetBytes($"mangahub-archive:{series.ExternalId}:{chapter.SourceId}")));
        var exists = await db.AdminIssues.AnyAsync(issue => issue.Kind == AdminIssueTypes.ArchiveIntegrity
            && issue.SubjectType == AdminIssueTypes.MaintenanceTask
            && issue.SubjectId == subjectId
            && issue.Status == "open", cancellationToken);
        if (exists) return;

        db.AdminIssues.Add(new AdminIssue
        {
            Kind = AdminIssueTypes.ArchiveIntegrity,
            SubjectType = AdminIssueTypes.MaintenanceTask,
            SubjectId = subjectId,
            Priority = "high",
            TitleSnapshot = $"Archived chapter failed validation: {series.Title} Ch. {chapter.ChapterNumber}",
            MetadataJson = JsonSerializer.Serialize(new { series.ExternalId, chapter.SourceId, chapter.ChapterNumber, chapter.Language, Error = error }),
            ResolutionNote = "Restore or re-download this chapter from the catalog cache manager, then resolve this issue."
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsMangaDexOngoing(string? status) =>
        string.Equals(status?.Trim(), "ongoing", StringComparison.OrdinalIgnoreCase);

    private static bool IsMangaDexHiatus(string? status) =>
        string.Equals(status?.Trim(), "hiatus", StringComparison.OrdinalIgnoreCase);

    private async Task CreateReleaseNotificationsAsync(
        MangaHubDbContext db,
        MangaEntry entry,
        IReadOnlyList<(string Language, decimal Chapter)> releases,
        CancellationToken cancellationToken)
    {
        foreach (var (language, chapter) in releases)
        {
            var recipients = await (
                from shelf in db.UserMangaEntries
                join user in db.Users on shelf.UserId equals user.Id
                where shelf.MangaEntryId == entry.Id
                    && (shelf.ReadingStatus == "reading" || shelf.ReadingStatus == "paused")
                select new { shelf.UserId, shelf.CurrentChapter, user.PreferredLanguage })
                .ToListAsync(cancellationToken);

            foreach (var recipient in recipients)
            {
                if (!LanguagePreferences.Contains(LanguagePreferences.Parse(recipient.PreferredLanguage), language))
                {
                    continue;
                }

                var currentChapter = ParseChapterNumber(recipient.CurrentChapter);
                if (currentChapter is null || chapter <= currentChapter.Value)
                {
                    continue;
                }

                var exists = await db.Notifications.AnyAsync(notification =>
                    notification.UserId == recipient.UserId
                    && notification.MangaEntryId == entry.Id
                    && notification.Type == "new-chapter"
                    && notification.Language == language
                    && notification.ChapterNumber == chapter, cancellationToken);
                if (exists)
                {
                    continue;
                }

                var notification = new MangaNotification
                {
                    UserId = recipient.UserId,
                    MangaEntryId = entry.Id,
                    Type = "new-chapter",
                    ChapterNumber = chapter,
                    Language = language,
                    Title = $"New chapter: {entry.Title}",
                    Body = $"Chapter {chapter:0.###} is available in {language}."
                };
                db.Notifications.Add(notification);
                await SendPushAsync(db, notification, cancellationToken);
            }
        }
    }

    private async Task SendPushAsync(MangaHubDbContext db, MangaNotification notification, CancellationToken cancellationToken)
    {
        var push = options.Value.WebPush;
        if (string.IsNullOrWhiteSpace(push.PublicKey) || string.IsNullOrWhiteSpace(push.PrivateKey)) return;
        var subscriptions = await db.WebPushSubscriptions.Where(subscription => subscription.UserId == notification.UserId).ToListAsync(cancellationToken);
        var payload = JsonSerializer.Serialize(new
        {
            title = notification.Title,
            body = notification.Body,
            url = $"/library?readEntryId={notification.MangaEntryId}&chapter={notification.ChapterNumber:0.###}&language={Uri.EscapeDataString(notification.Language)}&notificationId={notification.Id}"
        });
        var client = new WebPushClient();
        foreach (var subscription in subscriptions)
        {
            try { await client.SendNotificationAsync(new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth), payload, new VapidDetails(push.Subject, push.PublicKey, push.PrivateKey)); }
            catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound) { db.WebPushSubscriptions.Remove(subscription); }
            catch (WebPushException ex) { logger.LogWarning(ex, "Web push failed for notification {NotificationId}.", notification.Id); }
        }
    }

    private async Task PrefetchNewMangaDexChaptersAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MangaDexEnabled)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
        var mangaDex = scope.ServiceProvider.GetRequiredService<MangaSourceRegistry>().Get("mangadex");
        var cache = scope.ServiceProvider.GetRequiredService<IMangaDexChapterCache>();
        var batchSize = Math.Clamp(options.Value.MangaDexUpdatePrefetchBatchSize, 1, 100);
        var candidates = await (
            from shelf in db.UserMangaEntries
            join user in db.Users on shelf.UserId equals user.Id
            join entry in db.MangaEntries on shelf.MangaEntryId equals entry.Id
            where entry.MangaDexId != ""
                && (shelf.ReadingStatus == "reading" || shelf.ReadingStatus == "paused")
                && shelf.CurrentChapter != ""
            select new UpdatePrefetchCandidate(entry, shelf.CurrentChapter, shelf.IsRead, user.PreferredLanguage))
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        var mangaEntryIds = candidates.Select(candidate => candidate.Entry.Id).Distinct().ToArray();
        var latestChapters = await db.MangaDexLanguageLatestChapters
            .Where(latest => mangaEntryIds.Contains(latest.MangaEntryId))
            .ToListAsync(cancellationToken);

        var updateCandidates = candidates
            .Where(candidate => IsShelfUpdateCandidate(candidate, latestChapters))
            .GroupBy(candidate => candidate.Entry.Id)
            .OrderBy(group => group.Key)
            .Take(batchSize)
            .ToList();

        var downloaded = 0;
        foreach (var entryCandidates in updateCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entryCandidates.First().Entry;
            try
            {
                var sourceChapters = await mangaDex.GetChaptersAsync(entry.MangaDexId, null, cancellationToken);
                var nextReadableChapters = entryCandidates
                    .Select(candidate => FindNextReadableUpdateChapter(sourceChapters, candidate.CurrentChapter, candidate.IsRead, LanguagePreferences.Parse(candidate.PreferredLanguage)))
                    .Where(chapter => chapter is not null)
                    .Select(chapter => chapter!)
                    .GroupBy(chapter => chapter.Id, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToList();
                if (nextReadableChapters.Count == 0)
                {
                    continue;
                }

                var cacheSeries = await GetOrCreateCachedSeriesAsync(db, entry, cancellationToken);
                foreach (var chapter in nextReadableChapters)
                {
                    if (cacheSeries.Chapters.Any(cached => cached.SourceId == chapter.Id
                        && string.Equals(cached.ImageQuality, "original", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    await CacheChapterAsync(db, cache, mangaDex, entry, cacheSeries, chapter, cancellationToken);
                    downloaded++;
                }

                entry.MangaDexLastPrefetchedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or IOException)
            {
                logger.LogWarning(ex, "MangaDex pre-download failed for {Title} ({MangaDexId}).", entry.Title, entry.MangaDexId);
            }
        }

        logger.LogInformation(
            "MangaDex update prefetch cached {ChapterCount} next readable chapters across {MangaCount} manga with shelf updates.",
            downloaded,
            updateCandidates.Count);
    }

    private async Task RunIdleBackfillAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MangaDexEnabled || !options.Value.MangaDexIdleBackfillEnabled)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();
        if (!await IsSiteIdleAsync(db, cancellationToken))
        {
            logger.LogDebug("MangaDex historical backfill skipped because the site is active.");
            return;
        }

        var batchSize = Math.Clamp(options.Value.MangaDexIdleBackfillBatchSize, 1, 5);
        var perMangaLimit = Math.Clamp(options.Value.MangaDexIdleBackfillMaxChaptersPerManga, 1, 5);
        var entries = await db.MangaEntries
            .Where(entry => entry.MangaDexId != ""
                && db.UserMangaEntries.Any(shelf => shelf.MangaEntryId == entry.Id
                    && shelf.CurrentChapter != ""))
            .OrderBy(entry => entry.MangaDexLastBackfilledAt ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.Title)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
        {
            return;
        }

        var mangaDex = scope.ServiceProvider.GetRequiredService<MangaSourceRegistry>().Get("mangadex");
        var cache = scope.ServiceProvider.GetRequiredService<IMangaDexChapterCache>();
        var cachedChapters = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var currentChapters = await db.UserMangaEntries
                    .Where(shelf => shelf.MangaEntryId == entry.Id
                        && shelf.CurrentChapter != "")
                    .Select(shelf => shelf.CurrentChapter)
                    .ToListAsync(cancellationToken);
                var highestReadChapter = currentChapters
                    .Select(ParseChapterNumber)
                    .Where(number => number is not null)
                    .Select(number => number!.Value)
                    .DefaultIfEmpty()
                    .Max();
                if (highestReadChapter <= 0)
                {
                    entry.MangaDexLastBackfilledAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }

                var cacheSeries = await GetOrCreateCachedSeriesAsync(db, entry, cancellationToken);
                var cachedSourceIds = cacheSeries.Chapters.Select(chapter => chapter.SourceId).ToHashSet(StringComparer.Ordinal);
                var pending = GetPreferredNumberedChapters(
                        await mangaDex.GetChaptersAsync(entry.MangaDexId, null, cancellationToken))
                    .Where(item => item.Number <= highestReadChapter
                        && !cachedSourceIds.Contains(item.Chapter.Id))
                    .OrderByDescending(item => item.Number)
                    .Take(perMangaLimit)
                    .ToList();

                foreach (var item in pending)
                {
                    if (!await IsSiteIdleAsync(db, cancellationToken))
                    {
                        logger.LogInformation("MangaDex historical backfill paused because the site is active again.");
                        return;
                    }

                    await CacheChapterAsync(db, cache, mangaDex, entry, cacheSeries, item.Chapter, cancellationToken);
                    cachedChapters++;
                    await db.SaveChangesAsync(cancellationToken);
                }

                entry.MangaDexLastBackfilledAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or IOException)
            {
                logger.LogWarning(ex, "MangaDex historical backfill failed for {Title} ({MangaDexId}).", entry.Title, entry.MangaDexId);
            }
        }

        logger.LogInformation("MangaDex historical backfill cached {ChapterCount} chapters across {MangaCount} manga.", cachedChapters, entries.Count);
    }

    private async Task<bool> IsSiteIdleAsync(MangaHubDbContext db, CancellationToken cancellationToken)
    {
        var lastActivity = await db.SiteActivities.AsNoTracking()
            .Where(activity => activity.Id == SiteActivity.SingletonId)
            .Select(activity => (DateTimeOffset?)activity.LastActivityAt)
            .FirstOrDefaultAsync(cancellationToken);
        var idleFor = TimeSpan.FromMinutes(Math.Clamp(options.Value.MangaDexIdleMinutes, 5, 1440));
        return lastActivity is null || lastActivity <= DateTimeOffset.UtcNow.Subtract(idleFor);
    }

    private static async Task<MangaSeries> GetOrCreateCachedSeriesAsync(
        MangaHubDbContext db,
        MangaEntry entry,
        CancellationToken cancellationToken)
    {
        var cacheSeries = await db.Series.Include(series => series.Chapters)
            .FirstOrDefaultAsync(series => series.Source == MangaDexCacheSource && series.ExternalId == entry.MangaDexId, cancellationToken);
        if (cacheSeries is not null)
        {
            return cacheSeries;
        }

        cacheSeries = new MangaSeries
        {
            Title = entry.Title,
            Description = entry.Description,
            CoverUrl = entry.CoverUrl,
            Status = entry.PublishingStatus,
            Source = MangaDexCacheSource,
            ExternalId = entry.MangaDexId
        };
        db.Series.Add(cacheSeries);
        return cacheSeries;
    }

    private static async Task CacheChapterAsync(
        MangaHubDbContext db,
        IMangaDexChapterCache cache,
        IMangaSource mangaDex,
        MangaEntry entry,
        MangaSeries cacheSeries,
        MangaSourceChapter sourceChapter,
        CancellationToken cancellationToken,
        string imageQuality = "original")
    {
        var pages = await mangaDex.GetPagesAsync(sourceChapter.Id, cancellationToken, imageQuality);
        var archive = await cache.EnsureCachedAsync(entry.MangaDexId, sourceChapter.Id, pages, cancellationToken, imageQuality: imageQuality);
        var cachedChapter = cacheSeries.Chapters.FirstOrDefault(chapter => chapter.SourceId == sourceChapter.Id
            && string.Equals(chapter.ImageQuality, imageQuality, StringComparison.OrdinalIgnoreCase));
        if (cachedChapter is null)
        {
            cachedChapter = new MangaChapter
            {
                Series = cacheSeries,
                ChapterNumber = sourceChapter.Number,
                Language = sourceChapter.Language,
                Title = sourceChapter.Title,
                SourceId = sourceChapter.Id,
                PageCount = archive.PageCount,
                FileHash = archive.FileHash,
                ImageQuality = imageQuality
            };
            cacheSeries.Chapters.Add(cachedChapter);
            db.Chapters.Add(cachedChapter);
            return;
        }

        cachedChapter.ChapterNumber = sourceChapter.Number;
        cachedChapter.Language = sourceChapter.Language;
        cachedChapter.Title = sourceChapter.Title;
        cachedChapter.PageCount = archive.PageCount;
        cachedChapter.FileHash = archive.FileHash;
        cachedChapter.ImageQuality = imageQuality;
    }

    private TimeSpan GetDelayUntilNextMaintenance()
    {
        var timeZone = GetMaintenanceTimeZone();
        var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        var hour = Math.Clamp(options.Value.MangaDexMaintenanceHour, 0, 23);
        var next = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, hour, 0, 0, localNow.Offset);
        if (next <= localNow)
        {
            next = next.AddDays(1);
        }

        return next - localNow;
    }

    private TimeSpan GetReleasePollDelay() =>
        TimeSpan.FromMinutes(Math.Clamp(options.Value.MangaDexReleasePollMinutes, 5, 720));

    private int GetReleaseSyncBatchSize(int totalLinkedEntries)
    {
        var refreshHours = Math.Clamp(options.Value.MangaDexSyncIntervalHours, 1, 24);
        var runsPerDay = Math.Max(1, 24 / refreshHours);
        var requiredBatchSize = (int)Math.Ceiling(totalLinkedEntries / (decimal)runsPerDay);
        var maximumBatchSize = Math.Clamp(options.Value.MangaDexSyncMaxBatchSize, 1, 1000);
        var batchSize = Math.Clamp(Math.Max(options.Value.MangaDexSyncBatchSize, requiredBatchSize), 1, maximumBatchSize);
        if (requiredBatchSize > maximumBatchSize)
        {
            logger.LogWarning(
                "MangaDex has {EntryCount} linked entries, which requires batches of {RequiredBatchSize} to refresh all entries within {RefreshHours} hours. The configured maximum is {MaximumBatchSize}.",
                totalLinkedEntries,
                requiredBatchSize,
                refreshHours,
                maximumBatchSize);
        }

        return batchSize;
    }

    private TimeZoneInfo GetMaintenanceTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(options.Value.MangaDexMaintenanceTimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            logger.LogWarning("MangaDex maintenance timezone {TimeZone} was not found. Falling back to UTC.", options.Value.MangaDexMaintenanceTimeZone);
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            logger.LogWarning("MangaDex maintenance timezone {TimeZone} is invalid. Falling back to UTC.", options.Value.MangaDexMaintenanceTimeZone);
            return TimeZoneInfo.Utc;
        }
    }

    private static List<(MangaSourceChapter Chapter, decimal Number)> GetPreferredNumberedChapters(IReadOnlyList<MangaSourceChapter> chapters) =>
        chapters
            .Select(chapter => new { Chapter = chapter, Number = ParseChapterNumber(chapter.Number) })
            .Where(item => item.Number is not null)
            .GroupBy(item => item.Number!.Value)
            .Select(group => (
                Chapter: group
                    .OrderBy(item => string.Equals(item.Chapter.Language, "en", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(item => item.Chapter.Language, StringComparer.OrdinalIgnoreCase)
                    .Select(item => item.Chapter)
                    .First(),
                Number: group.Key))
            .OrderBy(item => item.Number)
            .ToList();

    private static bool IsShelfUpdateCandidate(UpdatePrefetchCandidate candidate, IReadOnlyList<MangaDexLanguageLatestChapter> latestChapters)
    {
        var currentChapter = ParseChapterNumber(candidate.CurrentChapter);
        if (currentChapter is null)
        {
            return false;
        }

        var languages = LanguagePreferences.Parse(candidate.PreferredLanguage);
        return latestChapters
            .Where(latest => latest.MangaEntryId == candidate.Entry.Id)
            .Where(latest => LanguagePreferences.Contains(languages, latest.Language))
            .Any(latest => latest.LatestChapter > currentChapter.Value
                || (latest.LatestChapter == currentChapter.Value && !candidate.IsRead));
    }

    private static MangaSourceChapter? FindNextReadableUpdateChapter(
        IReadOnlyList<MangaSourceChapter> chapters,
        string currentChapter,
        bool isRead,
        IReadOnlyList<string> preferredLanguages)
    {
        var currentNumber = ParseChapterNumber(currentChapter);
        if (currentNumber is null)
        {
            return null;
        }

        var readableChapters = chapters
            .Where(chapter => chapter.PageCount > 0)
            .Select(chapter => new { Chapter = chapter, Number = ParseChapterNumber(chapter.Number) })
            .Where(item => item.Number is not null)
            .ToList();

        if (!isRead)
        {
            // Match the reader: an exact chapter in any preferred language wins over a fallback chapter.
            foreach (var language in preferredLanguages)
            {
                var exact = readableChapters
                    .Where(item => string.Equals(item.Chapter.Language, language, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(item => item.Number == currentNumber.Value);
                if (exact is not null)
                {
                    return exact.Chapter;
                }
            }

            foreach (var language in preferredLanguages)
            {
                var fallback = readableChapters
                    .Where(item => string.Equals(item.Chapter.Language, language, StringComparison.OrdinalIgnoreCase))
                    .Where(item => item.Number >= currentNumber.Value)
                    .OrderBy(item => item.Number)
                    .Select(item => item.Chapter)
                    .FirstOrDefault();
                if (fallback is not null)
                {
                    return fallback;
                }
            }

            return null;
        }

        // For an already completed chapter, retain the account's language tier before chapter proximity.
        foreach (var language in preferredLanguages)
        {
            var next = readableChapters
                .Where(item => string.Equals(item.Chapter.Language, language, StringComparison.OrdinalIgnoreCase))
                .Where(item => item.Number > currentNumber.Value)
                .OrderBy(item => item.Number)
                .Select(item => item.Chapter)
                .FirstOrDefault();
            if (next is not null)
            {
                return next;
            }
        }

        return null;
    }

    private sealed record UpdatePrefetchCandidate(
        MangaEntry Entry,
        string CurrentChapter,
        bool IsRead,
        string PreferredLanguage);

    private static decimal? ParseChapterNumber(string value)
    {
        var normalized = new string((value ?? "")
            .Where(character => char.IsDigit(character) || character is '.' or ',')
            .ToArray())
            .Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private static int? ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    private static async Task<Dictionary<string, decimal>> GetLatestChapterNumbersByLanguageAsync(HttpClient client, string mangaDexId, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var latestByLanguage = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        var total = int.MaxValue;

        while (offset < total)
        {
            var path = $"/manga/{Uri.EscapeDataString(mangaDexId)}/feed?limit={pageSize}&offset={offset}&includeExternalUrl=0&order[chapter]=desc";
            using var response = await client.GetAsync(path, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            total = ReadInt(document.RootElement, "total") ?? 0;
            if (!document.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array
                || data.GetArrayLength() == 0)
            {
                break;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("attributes", out var attributes))
                {
                    continue;
                }

                // Match MangaDexSource: official/external entries may have a chapter number,
                // but no image pages and therefore cannot be opened in MangaHub's reader.
                if ((ReadInt(attributes, "pages") ?? 0) <= 0)
                {
                    continue;
                }

                var language = attributes.TryGetProperty("translatedLanguage", out var languageElement)
                    ? languageElement.GetString()?.Trim().ToLowerInvariant() ?? ""
                    : "";
                var chapter = attributes.TryGetProperty("chapter", out var chapterElement)
                    ? ParseChapterNumber(chapterElement.GetString() ?? "")
                    : null;
                if (string.IsNullOrWhiteSpace(language) || chapter is null)
                {
                    continue;
                }

                if (!latestByLanguage.TryGetValue(language, out var latest) || chapter.Value > latest)
                {
                    latestByLanguage[language] = chapter.Value;
                }
            }

            offset += data.GetArrayLength();
        }

        return latestByLanguage;
    }
}
