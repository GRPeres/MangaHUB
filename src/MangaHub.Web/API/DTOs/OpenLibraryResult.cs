namespace MangaHub.Web.API.DTOs;

public sealed record OpenLibraryResult(string Key, string Title, string Authors, string CoverUrl, int? FirstPublishYear, string Category, string Description);
