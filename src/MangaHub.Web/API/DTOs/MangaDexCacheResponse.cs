namespace MangaHub.Web.API.DTOs;

public sealed record CachedMangaDexChapterResponse(
    Guid Id,
    string ChapterNumber,
    string Language,
    string Title,
    int PageCount,
    DateTimeOffset CachedAt,
    bool IsManual);

public sealed record MangaDexCacheResponse(string MangaDexId, List<CachedMangaDexChapterResponse> Chapters);
public sealed record CacheMangaDexChapterRequest(string ChapterNumber, string Language = "en");
public sealed record UpdateCachedMangaDexChapterRequest(string ChapterNumber, string Language, string Title);
