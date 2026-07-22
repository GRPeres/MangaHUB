using MangaHub.Core.Sources;

namespace MangaHub.Infrastructure.Sources;

public sealed class LocalMangaSource : IMangaSource
{
    public string Name => "local";

    public Task<IReadOnlyList<MangaSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MangaSearchResult>>([]);

    public Task<MangaSourceSeries?> GetSeriesAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult<MangaSourceSeries?>(null);

    public Task<IReadOnlyList<MangaSourceChapter>> GetChaptersAsync(string seriesId, string? language, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MangaSourceChapter>>([]);

    public Task<IReadOnlyList<MangaPage>> GetPagesAsync(string chapterId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MangaPage>>([]);
}
