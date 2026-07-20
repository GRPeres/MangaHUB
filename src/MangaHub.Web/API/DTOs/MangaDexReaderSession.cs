namespace MangaHub.Web.API.DTOs;

public sealed record MangaDexReaderSession(
    Guid MangaEntryId,
    string Title,
    string CurrentChapter,
    MangaDexReaderChapter SelectedChapter,
    IReadOnlyList<MangaDexReaderChapter> Chapters);
