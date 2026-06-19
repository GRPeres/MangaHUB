using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.JSInterop;

namespace MangaHub.Web.Services;

public sealed class MangaHubApiClient(HttpClient http, IJSRuntime js)
{
    private const string StorageKey = "mangahub_session";
    private string sessionToken = "";

    public async Task<UserResponse?> RegisterAsync(string username, string password) =>
        await SendAsync<AuthRequest, UserResponse>(HttpMethod.Post, "/auth/register", new(username, password));

    public async Task<UserResponse?> LoginAsync(string username, string password) =>
        await SendAsync<AuthRequest, UserResponse>(HttpMethod.Post, "/auth/login", new(username, password));

    public async Task LogoutAsync() => await SendAsync<object, object>(HttpMethod.Post, "/auth/logout", new { });

    public async Task<UserResponse?> MeAsync() =>
        await GetAsync<UserResponse>("/auth/me");

    public async Task<List<UserAdminResponse>> GetUsersAsync() =>
        await GetAsync<List<UserAdminResponse>>("/api/admin/users") ?? [];

    public async Task<UserAdminResponse?> UpdateUserRoleAsync(Guid userId, string role) =>
        await SendAsync<UpdateUserRoleRequest, UserAdminResponse>(HttpMethod.Put, $"/api/admin/users/{userId}/role", new(role));

    public async Task<List<SeriesResponse>> GetSeriesAsync() =>
        await GetAsync<List<SeriesResponse>>("/api/series") ?? [];

    public async Task<List<MangaEntryResponse>> GetMangaEntriesAsync(string? status = null)
    {
        var query = string.IsNullOrWhiteSpace(status) ? "" : $"?status={Uri.EscapeDataString(status)}";
        return await GetAsync<List<MangaEntryResponse>>($"/api/manga{query}") ?? [];
    }

    public async Task<List<CatalogMangaResponse>> GetCatalogAsync(string? queryText = null)
    {
        var query = string.IsNullOrWhiteSpace(queryText) ? "" : $"?q={Uri.EscapeDataString(queryText)}";
        return await GetAsync<List<CatalogMangaResponse>>($"/api/catalog{query}") ?? [];
    }

    public async Task<List<OpenLibraryResult>> SearchOpenLibraryAsync(string query) =>
        await GetAsync<List<OpenLibraryResult>>($"/api/openlibrary/search?q={Uri.EscapeDataString(query)}") ?? [];

    public async Task<CatalogMangaResponse?> CreateCatalogMangaAsync(MangaEntryRequest request) =>
        await SendAsync<MangaEntryRequest, CatalogMangaResponse>(HttpMethod.Post, "/api/catalog", request);

    public async Task<MangaEntryResponse?> AddToShelfAsync(AddToShelfRequest request) =>
        await SendAsync<AddToShelfRequest, MangaEntryResponse>(HttpMethod.Post, "/api/shelf", request);

    public async Task<ShelfImportResponse?> ImportShelfAsync(ShelfImportRequest request) =>
        await SendAsync<ShelfImportRequest, ShelfImportResponse>(HttpMethod.Post, "/api/shelf/import", request);

    public async Task<ReadOptions?> GetReadOptionsAsync(Guid entryId) =>
        await GetAsync<ReadOptions>($"/api/manga/{entryId}/read-options");

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

    public void SetSessionToken(string token)
    {
        sessionToken = token;
    }

    private async Task<TResponse?> GetAsync<TResponse>(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AddAuthorizationAsync(request);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<TResponse>() : default;
    }

    private async Task<TResponse?> SendAsync<TRequest, TResponse>(HttpMethod method, string url, TRequest payload)
    {
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(payload) };
        await AddAuthorizationAsync(request);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<TResponse>() : default;
    }

    private async Task AddAuthorizationAsync(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            sessionToken = await ReadStoredTokenAsync();
        }

        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        }
    }

    private async Task<string> ReadStoredTokenAsync()
    {
        try
        {
            var token = await js.InvokeAsync<string>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }

            return await js.InvokeAsync<string>("sessionStorage.getItem", StorageKey) ?? "";
        }
        catch
        {
            return "";
        }
    }
}

public sealed record AuthRequest(string Username, string Password);
public sealed record UserResponse(Guid Id, string Username, string Role, string SessionToken);
public sealed record UserAdminResponse(Guid Id, string Username, string Role, DateTimeOffset CreatedAt);
public sealed record UpdateUserRoleRequest(string Role);
public sealed record OpenLibraryResult(string Key, string Title, string Authors, string CoverUrl, int? FirstPublishYear, string Category, string Description);
public sealed record MangaEntryRequest(
    string Title,
    string Authors,
    string Category,
    string Description,
    string CoverUrl,
    string OpenLibraryKey,
    int? FirstPublishYear,
    string ReadingStatus,
    string MangaDexUrl,
    Guid? LocalSeriesId,
    string Notes);
public sealed record MangaEntryResponse(
    Guid Id,
    string Title,
    string Authors,
    string CatalogCategory,
    string Description,
    string CoverUrl,
    string OpenLibraryKey,
    int? FirstPublishYear,
    string ReadingStatus,
    string MangaDexUrl,
    string MangaDexId,
    Guid? LocalSeriesId,
    string CurrentChapter,
    int? Score,
    string Category,
    string Summary,
    string Notes);
public sealed record CatalogMangaResponse(
    Guid Id,
    string Title,
    string Authors,
    string Category,
    string Description,
    string CoverUrl,
    string OpenLibraryKey,
    int? FirstPublishYear,
    string MangaDexUrl,
    string MangaDexId,
    Guid? LocalSeriesId,
    bool IsInMyShelf);
public sealed record AddToShelfRequest(
    Guid MangaEntryId,
    string ReadingStatus,
    string CurrentChapter,
    int? Score,
    string Category,
    string Summary,
    string Notes);
public sealed record ShelfImportRequest(string CsvText, bool CreateMissingCatalogEntries);
public sealed record ShelfImportResponse(int Imported, int CreatedCatalogEntries, int UpdatedShelfEntries, int Skipped, List<string> Messages);
public sealed record ReadOptions(
    Guid Id,
    string Title,
    bool HasMangaDex,
    string MangaDexUrl,
    bool HasLocal,
    string LocalReaderUrl);
public sealed record SeriesResponse(Guid Id, string Title, string Description, string CoverUrl, string Status, string Source, string ExternalId);
public sealed record ChapterResponse(Guid Id, Guid SeriesId, string ChapterNumber, string Title, int PageCount);
public sealed record SearchResult(string Id, string Title, string Description, string CoverUrl, string Status, string Source);
public sealed record LibraryScanResult(int SeriesCount, int ChapterCount);
