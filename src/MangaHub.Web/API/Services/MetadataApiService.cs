namespace MangaHub.Web.API.Services;

public sealed class MetadataApiService(ApiHttpClient api)
{
    public async Task<List<MetadataResult>> SearchAsync(string query, bool includeOpenLibrary = false)
    {
        var url = $"/api/metadata/search?q={Uri.EscapeDataString(query)}&includeOpenLibrary={includeOpenLibrary.ToString().ToLowerInvariant()}";
        return await api.GetAsync<List<MetadataResult>>(url) ?? [];
    }
}
