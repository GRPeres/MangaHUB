namespace MangaHub.Web.API.DTOs;

public sealed record MangaDexReaderProgressRequest(string ChapterId, int Page, bool Completed);
