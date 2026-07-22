namespace MangaHub.Core.Sources;

public interface IMangaSource
{
    string Name { get; }
    Task<IReadOnlyList<MangaSearchResult>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<MangaSourceSeries?> GetSeriesAsync(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<MangaSourceChapter>> GetChaptersAsync(string seriesId, string? language, CancellationToken cancellationToken);
    Task<IReadOnlyList<MangaPage>> GetPagesAsync(string chapterId, CancellationToken cancellationToken);
}

public sealed record MangaSearchResult(
    string Id,
    string Title,
    string Description,
    string CoverUrl,
    string Status,
    string Source);

public sealed record MangaSourceSeries(
    string Id,
    string Title,
    string Description,
    string CoverUrl,
    string Status,
    string Source);

public sealed record MangaSourceChapter(
    string Id,
    string Number,
    string Title,
    int PageCount,
    string Language = "en");

public sealed record MangaPage(int Index, string Url);
