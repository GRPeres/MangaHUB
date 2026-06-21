namespace MangaHub.Web.Services;

public sealed class LibraryApiService(ApiHttpClient api)
{
    public async Task<LibraryScanResult?> ScanAsync() =>
        await api.SendAsync<object, LibraryScanResult>(HttpMethod.Post, "/api/library/scan", new { });
}
