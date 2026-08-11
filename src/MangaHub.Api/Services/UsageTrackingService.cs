using System.Text.Json;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Services;

public sealed class UsageTrackingService(UsageRepository usage)
{
    private static readonly HashSet<string> ClientEvents = [UsageEventTypes.Search, UsageEventTypes.NotificationOpened, UsageEventTypes.ReaderSession];

    public async Task TrackAsync(Guid userId, string eventType, Guid? mangaEntryId, Guid? chapterId, string sessionId, string idempotencyKey, int? durationSeconds, CancellationToken cancellationToken)
    {
        if (!await usage.IsEnabledAsync(userId, cancellationToken) || string.IsNullOrWhiteSpace(eventType)) return;
        if (!string.IsNullOrWhiteSpace(idempotencyKey) && await usage.HasIdempotencyKeyAsync(userId, idempotencyKey, cancellationToken)) return;
        usage.Add(new UsageEvent
        {
            UserId = userId,
            EventType = eventType.Trim().ToLowerInvariant(),
            MangaEntryId = mangaEntryId,
            ChapterId = chapterId,
            SessionId = sessionId.Trim()[..Math.Min(sessionId.Trim().Length, 80)],
            IdempotencyKey = idempotencyKey.Trim()[..Math.Min(idempotencyKey.Trim().Length, 160)],
            DurationSeconds = durationSeconds is > 0 ? Math.Min(durationSeconds.Value, 3600) : null,
            MetadataJson = "{}"
        });
        await usage.SaveChangesAsync(cancellationToken);
    }

    public Task TrackAsync(Guid userId, string eventType, Guid? mangaEntryId, CancellationToken cancellationToken) =>
        TrackAsync(userId, eventType, mangaEntryId, null, "", "", null, cancellationToken);

    public async Task TrackClientAsync(Guid userId, UsageTelemetryRequest request, CancellationToken cancellationToken)
    {
        var eventType = request.EventType.Trim().ToLowerInvariant();
        if (!ClientEvents.Contains(eventType)) return;
        await TrackAsync(userId, eventType, request.MangaEntryId, request.ChapterId, request.SessionId, request.IdempotencyKey, request.DurationSeconds, cancellationToken);
    }

    public async Task SetEnabledAsync(MangaUser user, bool enabled, CancellationToken cancellationToken)
    {
        user.UsageAnalyticsEnabled = enabled;
        await usage.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> ExportAsync(Guid userId, CancellationToken cancellationToken)
    {
        var events = await usage.Events.AsNoTracking().Where(item => item.UserId == userId).OrderBy(item => item.OccurredAt).ToListAsync(cancellationToken);
        var summaries = await usage.Summaries.AsNoTracking().Where(item => item.UserId == userId).OrderBy(item => item.Date).ToListAsync(cancellationToken);
        return JsonSerializer.Serialize(new { exportedAt = DateTimeOffset.UtcNow, events, dailySummaries = summaries }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var events = await usage.Events.Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var summaries = await usage.Summaries.Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        usage.RemoveEvents(events);
        usage.RemoveSummaries(summaries);
        await usage.SaveChangesAsync(cancellationToken);
    }

    public async Task<UsageDashboardResponse> GetDashboardAsync(Guid userId, int days, CancellationToken cancellationToken)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-Math.Clamp(days, 1, 365) + 1));
        var summaries = await usage.Summaries.AsNoTracking().Where(item => item.UserId == userId && item.Date >= since).OrderBy(item => item.Date).ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayEvents = await usage.Events.AsNoTracking().Where(item => item.UserId == userId && item.OccurredAt >= DateTimeOffset.UtcNow.Date).ToListAsync(cancellationToken);
        if (todayEvents.Count > 0)
        {
            summaries.RemoveAll(item => item.Date == today);
            summaries.Add(new UsageDailySummary
            {
                UserId = userId,
                Date = today,
                ReaderSeconds = todayEvents.Where(item => item.EventType == UsageEventTypes.ReaderSession).Sum(item => item.DurationSeconds ?? 0),
                ChaptersCompleted = todayEvents.Count(item => item.EventType == UsageEventTypes.ChapterCompleted),
                MangaStarted = todayEvents.Count(item => item.EventType == UsageEventTypes.MangaStarted),
                MangaCompleted = todayEvents.Count(item => item.EventType == UsageEventTypes.MangaCompleted),
                ShelfChanges = todayEvents.Count(item => item.EventType.StartsWith("shelf.", StringComparison.Ordinal)),
                CatalogChanges = todayEvents.Count(item => item.EventType.StartsWith("catalog.", StringComparison.Ordinal)),
                Searches = todayEvents.Count(item => item.EventType == UsageEventTypes.Search),
                NotificationOpens = todayEvents.Count(item => item.EventType == UsageEventTypes.NotificationOpened),
                SignIns = todayEvents.Count(item => item.EventType == UsageEventTypes.SignIn)
            });
            summaries = summaries.OrderBy(item => item.Date).ToList();
        }
        var dates = summaries.Where(item => item.ReaderSeconds > 0 || item.ChaptersCompleted > 0).Select(item => item.Date).ToHashSet();
        var streak = 0; for (var day = DateOnly.FromDateTime(DateTime.UtcNow); dates.Contains(day); day = day.AddDays(-1)) streak++;
        var topManga = await usage.Events.AsNoTracking().Where(item => item.UserId == userId && item.MangaEntryId != null && item.OccurredAt >= DateTimeOffset.UtcNow.AddDays(-90))
            .GroupBy(item => item.MangaEntryId!.Value).OrderByDescending(group => group.Count()).Take(5).Select(group => group.Key.ToString()).ToListAsync(cancellationToken);
        return new UsageDashboardResponse(summaries.Select(item => new UsageDailySummaryResponse(item.Date, item.ReaderSeconds, item.ChaptersCompleted, item.MangaStarted, item.MangaCompleted, item.ShelfChanges, item.CatalogChanges, item.Searches, item.NotificationOpens, item.SignIns)).ToList(), dates.Count, streak, topManga);
    }
}
