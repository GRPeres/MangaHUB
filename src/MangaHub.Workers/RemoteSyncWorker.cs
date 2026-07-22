using System.Globalization;
using System.Text.Json;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using MangaHub.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MangaHub.Workers;

public sealed class RemoteSyncWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<MangaHubOptions> options,
    ILogger<RemoteSyncWorker> logger) : BackgroundService
{
    private const string MangaDexCacheSource = "mangadex-cache";
    private static readonly TimeSpan FailedMaintenanceRetryDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextMaintenanceAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow >= nextMaintenanceAt)
            {
                if (!await RunMaintenanceAsync(stoppingToken))
                {
                    logger.LogWarning(
                        "MangaDex maintenance will retry in {RetryDelay} after an infrastructure failure.",
                        FailedMaintenanceRetryDelay);
                    await Task.Delay(FailedMaintenanceRetryDelay, stoppingToken);
                    continue;
                }

                nextMaintenanceAt = DateTimeOffset.UtcNow.Add(GetDelayUntilNextMaintenance());
                logger.LogInformation("Next MangaDex maintenance run is scheduled for {ScheduledAt}.", nextMaintenanceAt);
                continue;
            }

            var idleCheckDelay = TimeSpan.FromMinutes(Math.Clamp(options.Value.MangaDexIdleBackfillCheckMinutes, 5, 720));
            var delay = new[] { nextMaintenanceAt - DateTimeOffset.UtcNow, idleCheckDelay }.Min();
            await Task.Delay(delay, stoppingToken);

            if (DateTimeOffset.UtcNow < nextMaintenanceAt)
            {
                await RunIdleBackfillAsync(stoppingToken);
            }
        }
    }

    private async Task<bool> RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SyncMangaDexCatalogAsync(cancellationToken);
            await PrefetchNewMangaDexChaptersAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MangaDex maintenance run failed.");
            return false;
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
        var batchSize = Math.Clamp(options.Value.MangaDexSyncBatchSize, 1, 100);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MangaHubDbContext>();

        var entries = await db.MangaEntries
            .Where(entry => entry.MangaDexId != "" && (entry.MangaDexLastSyncedAt == null || entry.MangaDexLastSyncedAt < cutoff))
            .OrderBy(entry => entry.MangaDexLastSyncedAt ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.Title)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
        {
            logger.LogInformation("MangaDex catalog sync found no stale catalog entries.");
            return;
        }

        var client = httpClientFactory.CreateClient("mangadex-sync");
        var delay = TimeSpan.FromMilliseconds(Math.Max(250, options.Value.MangaDexSyncDelayMilliseconds));
        var updated = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var latestChapter = await GetLatestChapterNumberAsync(client, entry.MangaDexId, cancellationToken);
                entry.MangaDexLastSyncedAt = DateTimeOffset.UtcNow;

                if (latestChapter is not null && latestChapter != entry.ChapterCount)
                {
                    entry.ChapterCount = latestChapter;
                    entry.UpdatedAt = DateTimeOffset.UtcNow;
                    updated++;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
            {
                logger.LogWarning(ex, "MangaDex catalog sync failed for {Title} ({MangaDexId}).", entry.Title, entry.MangaDexId);
                entry.MangaDexLastSyncedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken);
            await Task.Delay(delay, cancellationToken);
        }

        logger.LogInformation("MangaDex catalog sync checked {CheckedCount} entries and updated {UpdatedCount}.", entries.Count, updated);
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
        var delay = TimeSpan.FromMilliseconds(Math.Max(1000, options.Value.MangaDexPrefetchDelayMilliseconds));

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
                var chapters = await mangaDex.GetChaptersAsync(entry.MangaDexId, cancellationToken);
                var numberedChapters = chapters
                    .Select(chapter => new { Chapter = chapter, Number = ParseChapterNumber(chapter.Number) })
                    .Where(item => item.Number is not null)
                    .OrderBy(item => item.Number)
                    .ToList();

                if (entry.MangaDexLastPrefetchedChapter is null)
                {
                    entry.MangaDexLastPrefetchedChapter = numberedChapters.LastOrDefault()?.Number;
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

                    entry.MangaDexLastPrefetchedChapter = item.Number!.Value;
                    entry.MangaDexLastPrefetchedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                    downloaded++;
                    await Task.Delay(delay, cancellationToken);
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
        var delay = TimeSpan.FromMilliseconds(Math.Max(3000, options.Value.MangaDexIdleBackfillDelayMilliseconds));
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
                var pending = (await mangaDex.GetChaptersAsync(entry.MangaDexId, cancellationToken))
                    .Select(chapter => new { Chapter = chapter, Number = ParseChapterNumber(chapter.Number) })
                    .Where(item => item.Number is not null
                        && item.Number.Value <= highestReadChapter
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
                    await Task.Delay(delay, cancellationToken);
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

    private static decimal? ParseChapterNumber(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static async Task<int?> GetLatestChapterNumberAsync(HttpClient client, string mangaDexId, CancellationToken cancellationToken)
    {
        var path = $"/manga/{Uri.EscapeDataString(mangaDexId)}/feed?limit=1&translatedLanguage[]=en&includeExternalUrl=0&order[chapter]=desc";
        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
        {
            return null;
        }

        var attributes = data[0].GetProperty("attributes");
        if (!attributes.TryGetProperty("chapter", out var chapterElement))
        {
            return null;
        }

        var chapter = chapterElement.GetString();
        if (!decimal.TryParse(chapter, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return (int)Math.Floor(value);
    }
}
