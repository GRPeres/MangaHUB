namespace MangaHub.Web.API.DTOs;

public sealed record CatalogMangaResponse(
    Guid Id,
    string Title,
    string Authors,
    string Category,
    string Description,
    string CoverUrl,
    string OpenLibraryKey,
    int? FirstPublishYear,
    string MangaDexUrl,
    string MangaDexId,
    Guid? LocalSeriesId,
    bool IsInMyShelf);
