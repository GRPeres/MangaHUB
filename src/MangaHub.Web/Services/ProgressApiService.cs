namespace MangaHub.Web.Services;

public sealed class ProgressApiService(ApiHttpClient api)
{
    public async Task<ProgressResponse?> SaveAsync(ProgressRequest request) =>
        await api.SendAsync<ProgressRequest, ProgressResponse>(HttpMethod.Post, "/api/progress", request);

    public async Task<List<ProgressResponse>> ListAsync() =>
        await api.GetAsync<List<ProgressResponse>>("/api/progress") ?? [];
}
