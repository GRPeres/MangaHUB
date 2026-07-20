namespace MangaHub.Web.API.DTOs;

public sealed record MangaDexReaderProgressResponse(string CurrentChapter, string ReadingStatus, int Page, bool Completed);
