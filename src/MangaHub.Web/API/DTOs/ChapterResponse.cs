namespace MangaHub.Web.API.DTOs;

public sealed record ChapterResponse(Guid Id, Guid SeriesId, string ChapterNumber, string Title, int PageCount);
