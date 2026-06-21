namespace MangaHub.Web.API.DTOs;

public sealed record ProgressRequest(Guid SeriesId, Guid ChapterId, int Page);
