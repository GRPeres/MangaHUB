namespace MangaHub.Core.Dto;

public sealed record AuthRequest(string Username, string Password);
public sealed record UserResponse(Guid Id, string Username, string Role, string SessionToken);
public sealed record UserAdminResponse(Guid Id, string Username, string Role, DateTimeOffset CreatedAt);
public sealed record UpdateUserRoleRequest(string Role);
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
    string MangaDexUrl,
    Guid? LocalSeriesId,
    string Notes,
    string MetadataSource = "",
    string MyAnimeListId = "",
    string MediaType = "",
    string PublishingStatus = "",
    int? ChapterCount = null,
    int? VolumeCount = null);
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
    string MangaDexUrl,
    string MangaDexId,
    decimal? MangaDexLatestChapter,
    DateTimeOffset? MangaDexLastSyncedAt,
    Guid? LocalSeriesId,
    string CurrentChapter,
    int? Score,
    string Category,
    string Summary,
    string Notes);
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
    string MangaDexUrl,
    string MangaDexId,
    decimal? MangaDexLatestChapter,
    DateTimeOffset? MangaDexLastSyncedAt,
    Guid? LocalSeriesId,
    int CachedChapterCount,
    bool IsInMyShelf);
public sealed record AddToShelfRequest(
    Guid MangaEntryId,
    string ReadingStatus,
    string CurrentChapter,
    int? Score,
    string Category,
    string Summary,
    string Notes);
public sealed record ShelfImportRequest(string CsvText, bool CreateMissingCatalogEntries);
public sealed record ShelfImportResponse(int Imported, int CreatedCatalogEntries, int UpdatedShelfEntries, int Skipped, List<string> Messages);
public sealed record ReaderLaunchResponse(string ReaderUrl, string CurrentChapter, int PageCount);
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
    bool IsSeriesComplete = false);
public sealed record CachedMangaDexChapterResponse(Guid Id, string ChapterNumber, string Language, string Title, int PageCount, DateTimeOffset CachedAt, bool IsManual);
public sealed record MangaDexCacheResponse(string MangaDexId, List<CachedMangaDexChapterResponse> Chapters);
public sealed record MangaDexLanguagesResponse(string MangaDexId, List<string> Languages);
public sealed record CacheMangaDexChapterRequest(string ChapterNumber);
