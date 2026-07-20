namespace MangaHub.Web.API.DTOs;

public sealed record ReaderLaunchResponse(string ReaderUrl, string CurrentChapter, int PageCount);
