namespace MangaHub.Web.API.DTOs;

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
