namespace MangaHub.Web.API.DTOs;

public sealed record HomeDashboardMangaResponse(
    Guid Id,
    string Title,
    string CoverUrl,
    string ReadingStatus,
    string CurrentChapter,
    int? Score,
    string Category,
    string Summary,
    string Notes,
    string MediaType,
    int? FirstPublishYear,
    decimal? MangaDexPreferredLanguageLatestChapter,
    bool IsRead);

public sealed record HomeDashboardResponse(
    HomeDashboardMangaResponse? ContinueReading,
    int NewReleaseCount,
    List<HomeDashboardMangaResponse> NewReleases,
    int PlannedCount,
    List<HomeDashboardMangaResponse> Recommendations,
    List<HomeDashboardMangaResponse> PendingRatings);
