namespace MangaHub.Web.API.DTOs;

public sealed record ReadOptions(
    Guid Id,
    string Title,
    bool HasMangaDex,
    string FallbackReaderUrl,
    bool HasLocal,
    string LocalReaderUrl);
