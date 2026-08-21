namespace MangaHub.Core.Dto;

public sealed record AuthRequest(string Username, string Password, string Email = "");
public sealed record UserResponse(Guid Id, string Username, string Role, string PreferredLanguage, string SessionToken, string Email = "", bool HasPassword = true, bool HasGoogleSignIn = false, bool IsEmailVerified = false, string PendingEmail = "", bool UsageAnalyticsEnabled = false);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record UpdateAccountRequest(string Email, string CurrentPassword = "", string NewPassword = "");
public sealed record UserAdminResponse(Guid Id, string Username, string Role, DateTimeOffset CreatedAt);
public sealed record UpdateUserRoleRequest(string Role);
public sealed record UpdatePreferredLanguageRequest(string PreferredLanguage);
public sealed record UpdateUsageAnalyticsRequest(bool Enabled);
public sealed record UsageTelemetryRequest(string EventType, Guid? MangaEntryId = null, Guid? ChapterId = null, string SessionId = "", string IdempotencyKey = "", int? DurationSeconds = null);
public sealed record UsageDailySummaryResponse(DateOnly Date, int ReaderSeconds, int ChaptersCompleted, int MangaStarted, int MangaCompleted, int ShelfChanges, int CatalogChanges, int Searches, int NotificationOpens, int SignIns);
public sealed record UsageDashboardResponse(List<UsageDailySummaryResponse> Days, int ActiveDays, int CurrentStreak, List<string> TopMangaIds);
public sealed record MangaNotificationResponse(Guid Id, Guid MangaEntryId, string Type, decimal ChapterNumber, string Language, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
public sealed record WebPushSubscriptionRequest(string Endpoint, string P256dh, string Auth, string DeviceLabel = "");
public sealed record WebPushSubscriptionResponse(Guid Id, string DeviceLabel, DateTimeOffset UpdatedAt);
public sealed record DiagnosticResult(bool Success, string Message);
public sealed record OperationsOverviewResponse(int CatalogCount, int MangaDexLinkedCount, int MangaUpdatesLinkedCount, int CachedChapterCount, long CacheBytes, DateTimeOffset? LastMangaDexSyncAt, DateTimeOffset? LastMangaUpdatesSyncAt, DateTimeOffset? LastLibraryScanAt, int StaleMangaDexCount, int StaleMangaUpdatesCount, List<MaintenanceJobResponse> RecentJobs);
public sealed record MaintenanceJobResponse(Guid Id, string Type, string Status, DateTimeOffset RequestedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string Error);
public sealed record QueueMaintenanceJobRequest(string Type);
public sealed record SeriesResponse(Guid Id, string Title, string Description, string CoverUrl, string Status, string Source, string ExternalId);
public sealed record ChapterResponse(Guid Id, Guid SeriesId, string ChapterNumber, string Title, int PageCount);
public sealed record ProgressRequest(Guid SeriesId, Guid ChapterId, int Page);
public sealed record ProgressResponse(Guid SeriesId, Guid ChapterId, int Page);
public sealed record OpenLibraryResult(string Key, string Title, string Authors, string CoverUrl, int? FirstPublishYear, string Category, string Description);
public sealed record MetadataResult(
    string Source,
    string SourceId,
    string Title,
    string Authors,
    string CoverUrl,
    int? FirstPublishYear,
    string Category,
    string Description,
    string MediaType,
    string PublishingStatus,
    int? ChapterCount,
    int? VolumeCount,
    string OpenLibraryKey,
    string MyAnimeListId);
public sealed record MangaEntryRequest(
    string Title,
    string Authors,
    string Category,
    string Description,
    string CoverUrl,
    string OpenLibraryKey,
    int? FirstPublishYear,
    string ReadingStatus,
    string MangaDexId,
    Guid? LocalSeriesId,
    string Notes,
    string MetadataSource = "",
    string MyAnimeListId = "",
    string MediaType = "",
    string PublishingStatus = "",
    int? ChapterCount = null,
    int? VolumeCount = null,
    string MangaUpdatesId = "",
    string FallbackReaderUrl = "",
    string ReaderPreference = "mangahub",
    decimal? MangaDexPreferredLanguageLatestChapter = null);
public sealed record MangaEntryResponse(
    Guid Id,
    string Title,
    string Authors,
    string CatalogCategory,
    string Description,
    string CoverUrl,
    string OpenLibraryKey,
    int? FirstPublishYear,
    string MetadataSource,
    string MyAnimeListId,
    string MediaType,
    string PublishingStatus,
    int? ChapterCount,
    int? VolumeCount,
    string ReadingStatus,
    string MangaDexId,
    decimal? MangaDexLatestChapter,
    DateTimeOffset? MangaDexLastSyncedAt,
    string MangaUpdatesId,
    decimal? MangaUpdatesLatestChapter,
    string MangaUpdatesStatus,
    bool? MangaUpdatesCompleted,
    DateTimeOffset? MangaUpdatesLastSyncedAt,
    Guid? LocalSeriesId,
    string CurrentChapter,
    int? Score,
    string Category,
    string Summary,
    string Notes,
    string FallbackReaderUrl,
    string ReaderPreference = "mangahub",
    decimal? MangaDexPreferredLanguageLatestChapter = null,
    bool IsRead = false,
    bool IsManualReleaseCheckDue = false,
    DateTimeOffset? LastExternalReaderVerifiedAt = null);

public sealed record ExternalReaderCheckInResponse(
    Guid MangaEntryId,
    string Title,
    string CurrentChapter,
    string FallbackReaderUrl,
    DateTimeOffset OpenedAt);

public sealed record ShelfSectionSummaryResponse(
    int Updates,
    int NewReleases,
    int Untracked,
    int Planned,
    int Reading,
    int Paused,
    int Done,
    int Dropped,
    int All);
public sealed record CatalogMangaResponse(
    Guid Id,
    string Title,
    string Authors,
    string Category,
    string Description,
    string CoverUrl,
    string OpenLibraryKey,
    int? FirstPublishYear,
    string MetadataSource,
    string MyAnimeListId,
    string MediaType,
    string PublishingStatus,
    int? ChapterCount,
    int? VolumeCount,
    string MangaDexId,
    decimal? MangaDexLatestChapter,
    DateTimeOffset? MangaDexLastSyncedAt,
    string MangaUpdatesId,
    decimal? MangaUpdatesLatestChapter,
    string MangaUpdatesStatus,
    bool? MangaUpdatesCompleted,
    DateTimeOffset? MangaUpdatesLastSyncedAt,
    Guid? LocalSeriesId,
    int CachedChapterCount,
    bool IsInMyShelf,
    string FallbackReaderUrl,
    string ReaderPreference = "mangahub",
    decimal? MangaDexPreferredLanguageLatestChapter = null);
public sealed record AddToShelfRequest(
    Guid MangaEntryId,
    string ReadingStatus,
    string CurrentChapter,
    int? Score,
    string Category,
    string Summary,
    string Notes);
public sealed record ShelfImportRequest(string CsvText, bool CreateMissingCatalogEntries, Dictionary<string, string>? ColumnMappings = null);
public sealed record ShelfImportResponse(int Imported, int CreatedCatalogEntries, int UpdatedShelfEntries, int Skipped, List<string> Messages);
public sealed record ReaderLaunchResponse(string ReaderUrl, string CurrentChapter, int PageCount);
public sealed record ReaderChapterMatch(string RequestedChapter, string MatchedChapter, string Language);
public sealed record ReaderChapterJump(string CurrentChapter, string NextChapter, string Language, List<string> AlternativeLanguages);
public sealed record ReaderPreparationStatus(
    Guid JobId,
    string Stage,
    int Progress,
    int CompletedPages,
    int TotalPages,
    bool IsComplete,
    bool IsFailed,
    string Error,
    ReaderLaunchResponse? Launch,
    List<string>? AvailableLanguages = null,
    bool IsSeriesComplete = false,
    ReaderChapterMatch? ChapterMatch = null,
    ReaderChapterJump? ChapterJump = null);
public sealed record CachedMangaDexChapterResponse(Guid Id, string ChapterNumber, string Language, string Title, int PageCount, DateTimeOffset CachedAt, bool IsManual);
public sealed record MangaDexCacheResponse(string MangaDexId, List<CachedMangaDexChapterResponse> Chapters);
public sealed record MangaDexLanguagesResponse(string MangaDexId, List<string> Languages);
public sealed record CacheMangaDexChapterRequest(string ChapterNumber, string Language = "en");
public sealed record UpdateCachedMangaDexChapterRequest(string ChapterNumber, string Language, string Title);
