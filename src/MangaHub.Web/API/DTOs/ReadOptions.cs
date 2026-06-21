namespace MangaHub.Web.API.DTOs;

public sealed record ReadOptions(
    Guid Id,
    string Title,
    bool HasMangaDex,
    string MangaDexUrl,
    bool HasLocal,
    string LocalReaderUrl);
