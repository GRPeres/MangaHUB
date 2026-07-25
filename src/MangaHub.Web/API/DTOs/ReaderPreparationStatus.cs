namespace MangaHub.Web.API.DTOs;

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
