using System.Globalization;
using System.Text.Json;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MangaHub.Workers;

public sealed class RemoteSyncWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<MangaHubOptions> options,
    ILogger<RemoteSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SyncMangaDexCatalogAsync(stoppingToken);

        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.MangaDexSyncIntervalHours));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SyncMangaDexCatalogAsync(stoppingToken);
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

                if (latestChapter is not null && latestChapter > (entry.ChapterCount ?? 0))
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

        return (int)Math.Ceiling(value);
    }
}
