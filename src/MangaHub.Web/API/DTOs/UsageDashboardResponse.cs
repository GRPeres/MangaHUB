namespace MangaHub.Web.API.DTOs;

public sealed record UsageDailySummaryResponse(DateOnly Date, int ReaderSeconds, int ChaptersCompleted, int MangaStarted, int MangaCompleted, int ShelfChanges, int CatalogChanges, int Searches, int NotificationOpens, int SignIns);
public sealed record UsageDashboardResponse(List<UsageDailySummaryResponse> Days, int ActiveDays, int CurrentStreak, List<string> TopMangaIds);
