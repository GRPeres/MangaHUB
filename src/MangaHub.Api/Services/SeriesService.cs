using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Infrastructure.Sources;

namespace MangaHub.Api.Services;

public sealed class SeriesService(SeriesRepository series, MangaSourceRegistry sources)
{
    public Task<List<SeriesResponse>> ListAsync(string? title, string? source, string? status, CancellationToken cancellationToken) =>
        series.ListAsync(title, source, status, cancellationToken);

    public async Task<List<object>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var results = await series.SearchLocalAsync(query, cancellationToken);
        foreach (var source in sources.All.Where(x => x.Name != "local"))
        {
            results.AddRange(await source.SearchAsync(query, cancellationToken));
        }

        return results;
    }

    public Task<SeriesResponse?> GetAsync(Guid seriesId, CancellationToken cancellationToken) =>
        series.GetAsync(seriesId, cancellationToken);

    public Task<List<ChapterResponse>> ListChaptersAsync(Guid seriesId, CancellationToken cancellationToken) =>
        series.ListChaptersAsync(seriesId, cancellationToken);
}
