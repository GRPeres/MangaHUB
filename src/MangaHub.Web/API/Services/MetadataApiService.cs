namespace MangaHub.Web.API.Services;

public sealed class MetadataApiService(ApiHttpClient api)
{
    public async Task<List<MetadataResult>> SearchAsync(string query, bool includeOpenLibrary = false)
    {
        var url = $"/api/metadata/search?q={Uri.EscapeDataString(query)}&includeOpenLibrary={includeOpenLibrary.ToString().ToLowerInvariant()}";
        return await api.GetAsync<List<MetadataResult>>(url) ?? [];
    }

    public Task<MangaDexCatalogMatch?> FindMangaDexMatchAsync(string myAnimeListId, string title)
    {
        var url = $"/api/metadata/mangadex-match?malId={Uri.EscapeDataString(myAnimeListId)}&title={Uri.EscapeDataString(title)}";
        return api.GetAsync<MangaDexCatalogMatch>(url);
    }

    public Task<MangaUpdatesSearchResult?> FindMangaUpdatesMatchAsync(string title, string mediaType, int? firstPublishYear)
    {
        var year = firstPublishYear?.ToString() ?? "";
        var url = $"/api/metadata/mangaupdates-match?title={Uri.EscapeDataString(title)}&mediaType={Uri.EscapeDataString(mediaType)}&firstPublishYear={Uri.EscapeDataString(year)}";
        return api.GetAsync<MangaUpdatesSearchResult>(url);
    }

    public Task<MangaDexCatalogMatch?> FindMangaDexTitleMatchAsync(string title) =>
        api.GetAsync<MangaDexCatalogMatch>($"/api/metadata/mangadex-title-match?title={Uri.EscapeDataString(title)}");

    public Task<MetadataResult?> GetMangaDexMetadataAsync(string mangaDexId) =>
        api.GetAsync<MetadataResult>($"/api/metadata/mangadex/{Uri.EscapeDataString(mangaDexId)}");

    public Task<MetadataResult?> GetMangaUpdatesMetadataAsync(string mangaUpdatesId) =>
        api.GetAsync<MetadataResult>($"/api/metadata/mangaupdates/{Uri.EscapeDataString(mangaUpdatesId)}");
}
