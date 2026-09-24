namespace MangaHub.Web.API.DTOs;

public sealed record MaintenanceJobResponse(Guid Id, string Type, string Status, DateTimeOffset RequestedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string Error);
public sealed record OperationsOverviewResponse(int CatalogCount, int MangaDexLinkedCount, int MangaUpdatesLinkedCount, int CachedChapterCount, long CacheBytes, int ActiveCachedChapterCount, long ActiveCacheBytes, int ArchivedChapterCount, long ArchivedCacheBytes, DateTimeOffset? LastMangaDexSyncAt, DateTimeOffset? LastMangaUpdatesSyncAt, DateTimeOffset? LastLibraryScanAt, int StaleMangaDexCount, int StaleMangaUpdatesCount, List<MaintenanceJobResponse> RecentJobs);
