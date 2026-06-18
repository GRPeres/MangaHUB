using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace MangaHub.Web.Services;

public sealed class MangaHubApiClient(HttpClient http)
{
    public async Task<UserResponse?> RegisterAsync(string username, string password) =>
        await SendAsync<AuthRequest, UserResponse>(HttpMethod.Post, "/auth/register", new(username, password));

    public async Task<UserResponse?> LoginAsync(string username, string password) =>
        await SendAsync<AuthRequest, UserResponse>(HttpMethod.Post, "/auth/login", new(username, password));

    public async Task LogoutAsync() => await SendAsync<object, object>(HttpMethod.Post, "/auth/logout", new { });

    public async Task<List<SeriesResponse>> GetSeriesAsync() =>
        await GetAsync<List<SeriesResponse>>("/api/series") ?? [];

    public async Task<SeriesResponse?> GetSeriesAsync(Guid id) =>
        await GetAsync<SeriesResponse>($"/api/series/{id}");

    public async Task<List<ChapterResponse>> GetChaptersAsync(Guid seriesId) =>
        await GetAsync<List<ChapterResponse>>($"/api/series/{seriesId}/chapters") ?? [];

    public async Task<List<SearchResult>> SearchAsync(string query) =>
        await GetAsync<List<SearchResult>>($"/api/series/search?q={Uri.EscapeDataString(query)}") ?? [];

    public async Task<LibraryScanResult?> ScanAsync() =>
        await SendAsync<object, LibraryScanResult>(HttpMethod.Post, "/api/library/scan", new { });

    public string GetPageUrl(Guid chapterId, int pageIndex) =>
        new Uri(http.BaseAddress!, $"/api/read/{chapterId}/pages/{pageIndex}").ToString();

    private async Task<TResponse?> GetAsync<TResponse>(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<TResponse>() : default;
    }

    private async Task<TResponse?> SendAsync<TRequest, TResponse>(HttpMethod method, string url, TRequest payload)
    {
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(payload) };
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<TResponse>() : default;
    }
}

public sealed record AuthRequest(string Username, string Password);
public sealed record UserResponse(Guid Id, string Username);
public sealed record SeriesResponse(Guid Id, string Title, string Description, string CoverUrl, string Status, string Source, string ExternalId);
public sealed record ChapterResponse(Guid Id, Guid SeriesId, string ChapterNumber, string Title, int PageCount);
public sealed record SearchResult(string Id, string Title, string Description, string CoverUrl, string Status, string Source);
public sealed record LibraryScanResult(int SeriesCount, int ChapterCount);

