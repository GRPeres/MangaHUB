using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MangaHub.Web.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace MangaHub.Web.API;

public sealed class ApiHttpClient(HttpClient http, SessionTokenStore tokens)
{
    public async Task<TResponse?> GetAsync<TResponse>(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AddAuthorizationAsync(request);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode ? await ReadJsonOrDefaultAsync<TResponse>(response) : default;
    }

    public async Task<TResponse?> SendAsync<TRequest, TResponse>(HttpMethod method, string url, TRequest payload)
    {
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(payload) };
        await AddAuthorizationAsync(request);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode ? await ReadJsonOrDefaultAsync<TResponse>(response) : default;
    }

    public async Task<bool> SendWithoutResponseAsync<TRequest>(HttpMethod method, string url, TRequest payload)
    {
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(payload) };
        await AddAuthorizationAsync(request);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        await AddAuthorizationAsync(request);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    public async Task<TResponse?> SendMultipartAsync<TResponse>(string url, MultipartFormDataContent content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        await AddAuthorizationAsync(request);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode ? await ReadJsonOrDefaultAsync<TResponse>(response) : default;
    }

    public string GetAbsoluteUrl(string url) => new Uri(http.BaseAddress!, url).ToString();

    private async Task AddAuthorizationAsync(HttpRequestMessage request)
    {
        var sessionToken = await tokens.GetAsync();
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        }
    }

    private static async Task<TResponse?> ReadJsonOrDefaultAsync<TResponse>(HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            return default;
        }

        var json = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<TResponse>(json, JsonSerializerOptions.Web);
    }
}
