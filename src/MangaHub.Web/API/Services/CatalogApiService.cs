using Microsoft.AspNetCore.Components.Forms;
using MangaHub.Web.Services;

namespace MangaHub.Web.API.Services;

public sealed class CatalogApiService(ApiHttpClient api, AppRefreshService refreshes)
{
    public async Task<List<CatalogMangaResponse>> GetCatalogAsync(string? queryText = null, string? language = null, int offset = 0, int limit = 500)
    {
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(queryText)) queryParts.Add($"q={Uri.EscapeDataString(queryText)}");
        if (!string.IsNullOrWhiteSpace(language)) queryParts.Add($"language={Uri.EscapeDataString(language)}");
        queryParts.Add($"offset={Math.Max(offset, 0)}");
        queryParts.Add($"limit={Math.Clamp(limit, 1, 500)}");
        var query = queryParts.Count == 0 ? "" : $"?{string.Join('&', queryParts)}";
        return await api.GetAsync<List<CatalogMangaResponse>>($"/api/catalog{query}") ?? [];
    }

    public async Task<ApiCallResult<CatalogMangaResponse>> CreateCatalogMangaAsync(MangaEntryRequest request)
    {
        var result = await api.SendWithResultAsync<MangaEntryRequest, CatalogMangaResponse>(HttpMethod.Post, "/api/catalog", request);
        NotifyCatalogMutation(result.Value is not null);
        return result;
    }

    public async Task<ApiCallResult<CatalogMangaResponse>> UpdateCatalogMangaAsync(Guid entryId, MangaEntryRequest request)
    {
        var result = await api.SendWithResultAsync<MangaEntryRequest, CatalogMangaResponse>(HttpMethod.Put, $"/api/catalog/{entryId}", request);
        NotifyCatalogMutation(result.Value is not null);
        return result;
    }

    public async Task<MangaDexCacheResponse?> GetMangaDexCacheAsync(Guid entryId, string language) =>
        await api.GetAsync<MangaDexCacheResponse>($"/api/catalog/{entryId}/mangadex-cache?language={Uri.EscapeDataString(language)}");

    public async Task<MangaDexCacheResponse?> DownloadMangaDexChapterAsync(Guid entryId, string chapterNumber, string language)
    {
        var result = await api.SendAsync<CacheMangaDexChapterRequest, MangaDexCacheResponse>(
            HttpMethod.Post,
            $"/api/catalog/{entryId}/mangadex-cache/download",
            new CacheMangaDexChapterRequest(chapterNumber, language));
        NotifyCatalogMutation(result is not null);
        return result;
    }

    public async Task<MangaDexCacheResponse?> ImportMangaDexChapterAsync(Guid entryId, string chapterNumber, string title, string language, IBrowserFile file)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(chapterNumber), "chapterNumber");
        content.Add(new StringContent(title), "title");
        content.Add(new StringContent(language), "language");
        var fileContent = new StreamContent(file.OpenReadStream(1024L * 1024 * 1024));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType ?? "application/vnd.comicbook+zip");
        content.Add(fileContent, "file", file.Name);
        var result = await api.SendMultipartAsync<MangaDexCacheResponse>($"/api/catalog/{entryId}/mangadex-cache/import", content);
        NotifyCatalogMutation(result is not null);
        return result;
    }

    public async Task<MangaDexCacheResponse?> UpdateMangaDexChapterAsync(Guid entryId, Guid chapterId, UpdateCachedMangaDexChapterRequest request)
    {
        var result = await api.SendAsync<UpdateCachedMangaDexChapterRequest, MangaDexCacheResponse>(HttpMethod.Put, $"/api/catalog/{entryId}/mangadex-cache/{chapterId}", request);
        NotifyCatalogMutation(result is not null);
        return result;
    }

    public async Task<bool> DeleteMangaDexChapterAsync(Guid entryId, Guid chapterId)
    {
        var deleted = await api.DeleteAsync($"/api/catalog/{entryId}/mangadex-cache/{chapterId}");
        NotifyCatalogMutation(deleted);
        return deleted;
    }

    private void NotifyCatalogMutation(bool succeeded)
    {
        if (succeeded)
        {
            refreshes.Notify(AppRefreshScope.Catalog | AppRefreshScope.Shelf);
        }
    }
}

