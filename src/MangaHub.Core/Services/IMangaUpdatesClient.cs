namespace MangaHub.Core.Services;

public interface IMangaUpdatesClient
{
    Task<IReadOnlyList<MangaUpdatesSearchResult>> SearchSeriesAsync(string query, CancellationToken cancellationToken);
    Task<MangaUpdatesSeriesDetails?> GetSeriesAsync(string seriesId, CancellationToken cancellationToken);
}

public sealed record MangaUpdatesSearchResult(
    string Id,
    string Title,
    string Type,
    int? Year,
    IReadOnlyList<string> AlternativeTitles);

public sealed record MangaUpdatesSeriesDetails(
    string Id,
    string Title,
    decimal? LatestChapter,
    string Status,
    bool Completed);
