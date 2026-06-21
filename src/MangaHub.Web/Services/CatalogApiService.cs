namespace MangaHub.Web.Services;

public sealed class CatalogApiService(ApiHttpClient api)
{
    public async Task<List<CatalogMangaResponse>> GetCatalogAsync(string? queryText = null)
    {
        var query = string.IsNullOrWhiteSpace(queryText) ? "" : $"?q={Uri.EscapeDataString(queryText)}";
        return await api.GetAsync<List<CatalogMangaResponse>>($"/api/catalog{query}") ?? [];
    }

    public async Task<CatalogMangaResponse?> CreateCatalogMangaAsync(MangaEntryRequest request) =>
        await api.SendAsync<MangaEntryRequest, CatalogMangaResponse>(HttpMethod.Post, "/api/catalog", request);

    public async Task<CatalogMangaResponse?> UpdateCatalogMangaAsync(Guid entryId, MangaEntryRequest request) =>
        await api.SendAsync<MangaEntryRequest, CatalogMangaResponse>(HttpMethod.Put, $"/api/catalog/{entryId}", request);
}
