using Microsoft.AspNetCore.Components.Forms;

namespace MangaHub.Web.API.Services;

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

    public async Task<MangaDexCacheResponse?> GetMangaDexCacheAsync(Guid entryId) =>
        await api.GetAsync<MangaDexCacheResponse>($"/api/catalog/{entryId}/mangadex-cache");

    public async Task<MangaDexCacheResponse?> DownloadMangaDexChapterAsync(Guid entryId, string chapterNumber) =>
        await api.SendAsync<CacheMangaDexChapterRequest, MangaDexCacheResponse>(
            HttpMethod.Post,
            $"/api/catalog/{entryId}/mangadex-cache/download",
            new CacheMangaDexChapterRequest(chapterNumber));

    public async Task<MangaDexCacheResponse?> ImportMangaDexChapterAsync(Guid entryId, string chapterNumber, string title, IBrowserFile file)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(chapterNumber), "chapterNumber");
        content.Add(new StringContent(title), "title");
        var fileContent = new StreamContent(file.OpenReadStream(1024L * 1024 * 1024));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType ?? "application/vnd.comicbook+zip");
        content.Add(fileContent, "file", file.Name);
        return await api.SendMultipartAsync<MangaDexCacheResponse>($"/api/catalog/{entryId}/mangadex-cache/import", content);
    }

    public Task<bool> DeleteMangaDexChapterAsync(Guid entryId, Guid chapterId) =>
        api.DeleteAsync($"/api/catalog/{entryId}/mangadex-cache/{chapterId}");
}

