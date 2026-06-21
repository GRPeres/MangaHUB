namespace MangaHub.Web.API.DTOs;

public sealed record MangaEntryResponse(
    Guid Id,
    string Title,
    string Authors,
    string CatalogCategory,
    string Description,
    string CoverUrl,
    string OpenLibraryKey,
    int? FirstPublishYear,
    string ReadingStatus,
    string MangaDexUrl,
    string MangaDexId,
    Guid? LocalSeriesId,
    string CurrentChapter,
    int? Score,
    string Category,
    string Summary,
    string Notes);
