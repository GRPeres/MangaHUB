namespace MangaHub.Web.Services;

public sealed class OpenLibraryApiService(ApiHttpClient api)
{
    public async Task<List<OpenLibraryResult>> SearchOpenLibraryAsync(string query) =>
        await api.GetAsync<List<OpenLibraryResult>>($"/api/openlibrary/search?q={Uri.EscapeDataString(query)}") ?? [];
}
