namespace MangaHub.Web.API.DTOs;

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
