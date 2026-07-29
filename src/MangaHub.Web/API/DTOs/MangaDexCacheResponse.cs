namespace MangaHub.Web.API.DTOs;

public sealed record CachedMangaDexChapterResponse(
    Guid Id,
    string ChapterNumber,
    string Language,
    string Title,
    int PageCount,
    DateTimeOffset CachedAt,
    bool IsManual,
    string SourceLanguage = "",
    List<ChapterTranslationResponse>? Translations = null);

public sealed record ChapterTranslationResponse(string TargetLanguage, string Status, int PageCount, DateTimeOffset UpdatedAt, string Error);
public sealed record MangaDexCacheResponse(string MangaDexId, List<CachedMangaDexChapterResponse> Chapters);
public sealed record CacheMangaDexChapterRequest(string ChapterNumber);
