namespace MangaHub.Web.API.DTOs;

public sealed record SeriesResponse(Guid Id, string Title, string Description, string CoverUrl, string Status, string Source, string ExternalId);
