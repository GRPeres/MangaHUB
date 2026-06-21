namespace MangaHub.Web.API.DTOs;

public sealed record SearchResult(string Id, string Title, string Description, string CoverUrl, string Status, string Source);
