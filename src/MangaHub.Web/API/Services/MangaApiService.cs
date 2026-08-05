namespace MangaHub.Web.API.Services;

public sealed class MangaApiService(ApiHttpClient api)
{
    public async Task<List<MangaEntryResponse>> GetMangaEntriesAsync(string? status = null, Guid? userId = null, int offset = 0, int limit = 500)
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
        queryParts.Add($"offset={Math.Max(offset, 0)}");
        queryParts.Add($"limit={Math.Clamp(limit, 1, 500)}");

        var query = queryParts.Count == 0 ? "" : $"?{string.Join("&", queryParts)}";
        return await api.GetAsync<List<MangaEntryResponse>>($"/api/manga{query}") ?? [];
    }

    public async Task<ReadOptions?> GetReadOptionsAsync(Guid entryId) =>
        await api.GetAsync<ReadOptions>($"/api/manga/{entryId}/read-options");

    public async Task<MangaDexLanguagesResponse?> GetMangaDexLanguagesAsync(Guid entryId) =>
        await api.GetAsync<MangaDexLanguagesResponse>($"/api/manga/{entryId}/mangadex-reader/languages");

    public async Task<ReaderPreparationStatus?> StartMangaDexPreparationAsync(
        Guid entryId,
        Guid? afterCachedChapterId = null,
        Guid? beforeCachedChapterId = null,
        string language = "en",
        bool allowLanguageFallback = false,
        bool allowChapterJump = false,
        string? requestedChapter = null)
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
        queryValues.Add($"language={Uri.EscapeDataString(language)}");
        if (allowLanguageFallback)
        {
            queryValues.Add("allowLanguageFallback=true");
        }
        if (allowChapterJump)
        {
            queryValues.Add("allowChapterJump=true");
        }
        if (!string.IsNullOrWhiteSpace(requestedChapter))
        {
            queryValues.Add($"requestedChapter={Uri.EscapeDataString(requestedChapter)}");
        }

        var query = queryValues.Count == 0 ? "" : $"?{string.Join("&", queryValues)}";
        return await api.SendAsync<object, ReaderPreparationStatus>(
            HttpMethod.Post,
            $"/api/manga/{entryId}/mangadex-reader/prepare{query}",
            new { });
    }

    public async Task PrefetchNextMangaDexChapterAsync(Guid entryId, Guid currentCachedChapterId, string language = "en") =>
        await api.SendWithoutResponseAsync(
            HttpMethod.Post,
            $"/api/manga/{entryId}/mangadex-reader/prefetch-next?afterCachedChapterId={currentCachedChapterId}&language={Uri.EscapeDataString(language)}",
            new { });

    public async Task<bool> MarkCurrentChapterReadAsync(Guid entryId, Guid chapterId) =>
        await api.SendAsync<object, bool>(
            HttpMethod.Post,
            $"/api/manga/{entryId}/reader/current-chapter-read/{chapterId}",
            new { });

    public async Task<ReaderPreparationStatus?> GetMangaDexPreparationAsync(Guid jobId) =>
        await api.GetAsync<ReaderPreparationStatus>($"/api/manga/mangadex-reader/jobs/{jobId}");
}

