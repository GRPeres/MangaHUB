using System.Globalization;
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

namespace MangaHub.Workers;

public sealed class RemoteSyncWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<MangaHubOptions> options,
    RemoteJobPriorityContext priorityContext,
    ILogger<RemoteSyncWorker> logger) : BackgroundService
{
    private const string MangaDexCacheSource = "mangadex-cache";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextReleaseSyncAt = DateTimeOffset.MinValue;
        var nextPrefetchAt = DateTimeOffset.MinValue;
        var nextMangaUpdatesSyncAt = DateTimeOffset.MinValue;
        var nextMangaUpdatesMatchAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow >= nextReleaseSyncAt)
            {
                using (priorityContext.Push(RemoteJobPriority.ReleaseSync))
                {
                    await RunReleaseSyncAsync(stoppingToken);
                }
                nextReleaseSyncAt = DateTimeOffset.UtcNow.Add(GetReleasePollDelay());
            }

            if (DateTimeOffset.UtcNow >= nextPrefetchAt)
            {
                using (priorityContext.Push(RemoteJobPriority.Prefetch))
                {
                    await RunPrefetchAsync(stoppingToken);
                }
                nextPrefetchAt = DateTimeOffset.UtcNow.Add(GetDelayUntilNextMaintenance());
                logger.LogInformation("Next MangaDex pre-download maintenance is scheduled for {ScheduledAt}.", nextPrefetchAt);
            }

            if (DateTimeOffset.UtcNow >= nextMangaUpdatesMatchAt)
            {
                using (priorityContext.Push(RemoteJobPriority.Maintenance))
                {
                    await RunMangaUpdatesMatchingAsync(stoppingToken);
                }
                nextMangaUpdatesMatchAt = DateTimeOffset.UtcNow.AddMinutes(
                    Math.Clamp(options.Value.MangaUpdatesMatchPollMinutes, 5, 720));
            }

            if (DateTimeOffset.UtcNow >= nextMangaUpdatesSyncAt)
            {
                using (priorityContext.Push(RemoteJobPriority.ReleaseSync))
                {
                    await RunMangaUpdatesSyncAsync(stoppingToken);
                }
                nextMangaUpdatesSyncAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(options.Value.MangaUpdatesReleasePollMinutes, 15, 720));
            }

            var idleCheckDelay = TimeSpan.FromMinutes(Math.Clamp(options.Value.MangaDexIdleBackfillCheckMinutes, 5, 720));
            var delay = new[]
            {
                nextReleaseSyncAt - DateTimeOffset.UtcNow,
                nextPrefetchAt - DateTimeOffset.UtcNow,
                nextMangaUpdatesSyncAt - DateTimeOffset.UtcNow,
                nextMangaUpdatesMatchAt - DateTimeOffset.UtcNow,
                idleCheckDelay
            }.Min();
            await Task.Delay(delay, stoppingToken);

            if (DateTimeOffset.UtcNow < nextReleaseSyncAt && DateTimeOffset.UtcNow < nextPrefetchAt)
            {
                using (priorityContext.Push(RemoteJobPriority.Backfill))
                {
                    await RunIdleBackfillAsync(stoppingToken);
                }
            }
        }
    }

    private async Task RunReleaseSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SyncMangaDexCatalogAsync(cancellationToken);
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

    private async Task SyncMangaDexCatalogAsync(CancellationToken cancellationToken)
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
        var batchSize = GetReleaseSyncBatchSize(totalLinkedEntries);

        var entries = await db.MangaEntries
            .Where(entry => entry.MangaDexId != "" &&
                (entry.MangaDexLatestChapter == null
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
        var updated = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
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

        logger.LogInformation("MangaDex catalog sync checked {CheckedCount} entries and updated {UpdatedCount}.", entries.Count, updated);
    }

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
                    && shelf.ReadingStatus == "reading"
                    && user.PreferredLanguage == language
                select new { shelf.UserId, shelf.CurrentChapter })
                .ToListAsync(cancellationToken);

            foreach (var recipient in recipients)
            {
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
        var batchSize = Math.Clamp(options.Value.MangaDexPrefetchBatchSize, 1, 25);
        var perMangaLimit = Math.Clamp(options.Value.MangaDexPrefetchMaxChaptersPerManga, 1, 10);

        var entries = await db.MangaEntries
            .Where(entry => entry.MangaDexId != ""
                && db.UserMangaEntries.Any(shelf => shelf.MangaEntryId == entry.Id && shelf.ReadingStatus == "reading"))
            .OrderBy(entry => entry.MangaDexLastPrefetchedAt ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.Title)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var downloaded = 0;
        var baselined = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var numberedChapters = GetPreferredNumberedChapters(
                    await mangaDex.GetChaptersAsync(entry.MangaDexId, null, cancellationToken));

                if (entry.MangaDexLastPrefetchedChapter is null)
                {
                    entry.MangaDexLastPrefetchedChapter = numberedChapters.Count == 0 ? null : numberedChapters[^1].Number;
                    entry.MangaDexLastPrefetchedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                    baselined++;
                    continue;
                }

                var pending = numberedChapters
                    .Where(item => item.Number > entry.MangaDexLastPrefetchedChapter.Value)
                    .Take(perMangaLimit)
                    .ToList();
                if (pending.Count == 0)
                {
                    entry.MangaDexLastPrefetchedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }

                var cacheSeries = await GetOrCreateCachedSeriesAsync(db, entry, cancellationToken);

                foreach (var item in pending)
                {
                    await CacheChapterAsync(db, cache, mangaDex, entry, cacheSeries, item.Chapter, cancellationToken);

                    entry.MangaDexLastPrefetchedChapter = item.Number;
                    entry.MangaDexLastPrefetchedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                    downloaded++;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or IOException)
            {
                logger.LogWarning(ex, "MangaDex pre-download failed for {Title} ({MangaDexId}).", entry.Title, entry.MangaDexId);
            }
        }

        logger.LogInformation(
            "MangaDex pre-download baselined {BaselineCount} manga and cached {ChapterCount} new chapters across {MangaCount} reading manga.",
            baselined,
            downloaded,
            entries.Count);
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
        CancellationToken cancellationToken)
    {
        var pages = await mangaDex.GetPagesAsync(sourceChapter.Id, cancellationToken);
        var archive = await cache.EnsureCachedAsync(entry.MangaDexId, sourceChapter.Id, pages, cancellationToken);
        var cachedChapter = cacheSeries.Chapters.FirstOrDefault(chapter => chapter.SourceId == sourceChapter.Id);
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
                FileHash = archive.FileHash
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

    private static decimal? ParseChapterNumber(string value)
    {
        var normalized = new string((value ?? "")
            .Where(character => char.IsDigit(character) || character is '.' or ',')
            .ToArray())
            .Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private static async Task<Dictionary<string, decimal>> GetLatestChapterNumbersByLanguageAsync(HttpClient client, string mangaDexId, CancellationToken cancellationToken)
    {
        var path = $"/manga/{Uri.EscapeDataString(mangaDexId)}/feed?limit=100&includeExternalUrl=0&order[chapter]=desc";
        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
        {
            return [];
        }

        return data.EnumerateArray()
            .Where(item => item.TryGetProperty("attributes", out _))
            .Select(item => item.GetProperty("attributes"))
            .Select(attributes => new
            {
                Language = attributes.TryGetProperty("translatedLanguage", out var language) ? language.GetString() ?? "" : "",
                Number = attributes.TryGetProperty("chapter", out var chapter) ? ParseChapterNumber(chapter.GetString() ?? "") : null
            })
            .Where(item => item.Number is not null && !string.IsNullOrWhiteSpace(item.Language))
            .GroupBy(item => item.Language.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Max(item => item.Number!.Value), StringComparer.OrdinalIgnoreCase);
    }
}
