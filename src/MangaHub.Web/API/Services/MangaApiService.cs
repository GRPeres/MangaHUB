namespace MangaHub.Web.API.Services;

public sealed class MangaApiService(ApiHttpClient api)
{
    public async Task<List<MangaEntryResponse>> GetMangaEntriesAsync(string? status = null, Guid? userId = null)
    {
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(status))
        {
            queryParts.Add($"status={Uri.EscapeDataString(status)}");
        }
        if (userId is not null)
        {
            queryParts.Add($"userId={Uri.EscapeDataString(userId.Value.ToString())}");
        }

        var query = queryParts.Count == 0 ? "" : $"?{string.Join("&", queryParts)}";
        return await api.GetAsync<List<MangaEntryResponse>>($"/api/manga{query}") ?? [];
    }

    public async Task<ReadOptions?> GetReadOptionsAsync(Guid entryId) =>
        await api.GetAsync<ReadOptions>($"/api/manga/{entryId}/read-options");

    public async Task<ReaderLaunchResponse?> PrepareMangaDexChapterAsync(Guid entryId, Guid? afterCachedChapterId = null)
    {
        var query = afterCachedChapterId is null ? "" : $"?afterCachedChapterId={afterCachedChapterId}";
        return await api.SendAsync<object, ReaderLaunchResponse>(
            HttpMethod.Post,
            $"/api/manga/{entryId}/mangadex-reader/prepare{query}",
            new { });
    }
}

