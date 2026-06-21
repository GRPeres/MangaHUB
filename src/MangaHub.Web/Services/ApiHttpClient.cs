using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace MangaHub.Web.Services;

public sealed class ApiHttpClient(HttpClient http, SessionTokenStore tokens)
{
    public async Task<TResponse?> GetAsync<TResponse>(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AddAuthorizationAsync(request);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<TResponse>() : default;
    }

    public async Task<TResponse?> SendAsync<TRequest, TResponse>(HttpMethod method, string url, TRequest payload)
    {
        using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(payload) };
        await AddAuthorizationAsync(request);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<TResponse>() : default;
    }

    public async Task<bool> DeleteAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        await AddAuthorizationAsync(request);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode;
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
}
