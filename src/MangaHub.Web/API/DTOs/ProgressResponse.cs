namespace MangaHub.Web.API.DTOs;

public sealed record ProgressResponse(Guid SeriesId, Guid ChapterId, int Page);
