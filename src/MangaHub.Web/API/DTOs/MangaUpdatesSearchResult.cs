namespace MangaHub.Web.API.DTOs;

public sealed record MangaUpdatesSearchResult(
    string Id,
    string Title,
    string Type,
    int? Year,
    IReadOnlyList<string> AlternativeTitles);
