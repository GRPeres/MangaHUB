namespace MangaHub.Web.API.DTOs;

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
