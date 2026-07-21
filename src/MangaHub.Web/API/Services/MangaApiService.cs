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

    public async Task<ReaderPreparationStatus?> StartMangaDexPreparationAsync(
        Guid entryId,
        Guid? afterCachedChapterId = null,
        Guid? beforeCachedChapterId = null)
    {
        var queryValues = new List<string>();
        if (afterCachedChapterId is not null)
        {
            queryValues.Add($"afterCachedChapterId={afterCachedChapterId}");
        }
        if (beforeCachedChapterId is not null)
        {
            queryValues.Add($"beforeCachedChapterId={beforeCachedChapterId}");
        }

        var query = queryValues.Count == 0 ? "" : $"?{string.Join("&", queryValues)}";
        return await api.SendAsync<object, ReaderPreparationStatus>(
            HttpMethod.Post,
            $"/api/manga/{entryId}/mangadex-reader/prepare{query}",
            new { });
    }

    public async Task PrefetchNextMangaDexChapterAsync(Guid entryId, Guid currentCachedChapterId) =>
        await api.SendAsync<object, object>(
            HttpMethod.Post,
            $"/api/manga/{entryId}/mangadex-reader/prefetch-next?afterCachedChapterId={currentCachedChapterId}",
            new { });

    public async Task<ReaderPreparationStatus?> GetMangaDexPreparationAsync(Guid jobId) =>
        await api.GetAsync<ReaderPreparationStatus>($"/api/manga/mangadex-reader/jobs/{jobId}");
}

