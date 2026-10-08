namespace MangaHub.Web.API.DTOs;

public sealed record MaintenanceJobResponse(Guid Id, string Type, string Trigger, string Status, DateTimeOffset RequestedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string Error);
public sealed record ArchiveRecoveryTestResponse(bool Success, string Message, string Title = "", string ChapterNumber = "");
public sealed record OperationsOverviewResponse(int CatalogCount, int MangaDexLinkedCount, int MangaUpdatesLinkedCount, int CachedChapterCount, long CacheBytes, int ActiveCachedChapterCount, long ActiveCacheBytes, int ArchivedChapterCount, long ArchivedCacheBytes, DateTimeOffset? LastMangaDexSyncAt, DateTimeOffset? LastMangaUpdatesSyncAt, DateTimeOffset? LastLibraryScanAt, int StaleMangaDexCount, int StaleMangaUpdatesCount, List<MaintenanceJobResponse> RecentJobs, int OriginalCachedChapterCount, long OriginalCacheBytes, int DataSaverCachedChapterCount, long DataSaverCacheBytes, int ReclaimableActiveChapterCount, long ReclaimableActiveBytes, int ArchiveRestoreCountLast30Days, int ArchiveRedownloadCountLast30Days);
public sealed record ArchiveOverviewResponse(
    int ActiveChapterCount,
    long ActiveBytes,
    int ArchivedChapterCount,
    long ArchivedBytes,
    int ReadingProtectedMangaCount,
    int ReadingProtectedChapterCount,
    long ReadingProtectedBytes,
    int GracePeriodMangaCount,
    int GracePeriodChapterCount,
    long GracePeriodBytes,
    int ReadyToArchiveMangaCount,
    int ReadyToArchiveChapterCount,
    long ReadyToArchiveBytes,
    int UnmanagedActiveChapterCount,
    long UnmanagedActiveBytes,
    int GraceDays,
    int ArchiveRestoreCountLast30Days,
    int ArchiveRedownloadCountLast30Days,
    List<ArchiveMangaRetentionResponse> Manga);
public sealed record ArchiveMangaRetentionResponse(
    string MangaDexId,
    string Title,
    int ActiveChapterCount,
    long ActiveBytes,
    int ReadingProtectedChapterCount,
    int GracePeriodChapterCount,
    int ReadyToArchiveChapterCount,
    int UnmanagedActiveChapterCount,
    string? ReaderProtectionDetail,
    int ArchivedChapterCount,
    long ArchivedBytes);
