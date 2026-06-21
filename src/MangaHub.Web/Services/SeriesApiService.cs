namespace MangaHub.Web.Services;

public sealed class SeriesApiService(ApiHttpClient api)
{
    public async Task<List<SeriesResponse>> GetSeriesAsync() =>
        await api.GetAsync<List<SeriesResponse>>("/api/series") ?? [];

    public async Task<SeriesResponse?> GetSeriesAsync(Guid id) =>
        await api.GetAsync<SeriesResponse>($"/api/series/{id}");

    public async Task<List<ChapterResponse>> GetChaptersAsync(Guid seriesId) =>
        await api.GetAsync<List<ChapterResponse>>($"/api/series/{seriesId}/chapters") ?? [];

    public async Task<List<SearchResult>> SearchAsync(string query) =>
        await api.GetAsync<List<SearchResult>>($"/api/series/search?q={Uri.EscapeDataString(query)}") ?? [];
}
