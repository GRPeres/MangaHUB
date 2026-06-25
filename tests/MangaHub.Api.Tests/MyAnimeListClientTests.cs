using System.Net;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Sources;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Tests;

public sealed class MyAnimeListClientTests
{
    [Fact]
    public async Task SearchMangaAsync_RemovesMyAnimeListNsfwFilter()
    {
        var handler = new CaptureHandler("""
            {"data":[]}
            """);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.myanimelist.net/v2/")
        };
        var client = new MyAnimeListClient(httpClient, Options.Create(new MangaHubOptions { MyAnimeListClientId = "client-id" }));

        await client.SearchMangaAsync("berserk", CancellationToken.None);

        Assert.NotNull(handler.Request);
        Assert.Contains("nsfw=true", handler.Request!.RequestUri!.Query);
        Assert.True(handler.Request.Headers.TryGetValues("X-MAL-CLIENT-ID", out var values));
        Assert.Equal("client-id", Assert.Single(values));
    }

    [Fact]
    public async Task SearchMangaAsync_DoesNotCallApiWithoutClientId()
    {
        var handler = new CaptureHandler("""
            {"data":[]}
            """);
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.myanimelist.net/v2/")
        };
        var client = new MyAnimeListClient(httpClient, Options.Create(new MangaHubOptions { MyAnimeListClientId = "" }));

        var results = await client.SearchMangaAsync("berserk", CancellationToken.None);

        Assert.Empty(results);
        Assert.Null(handler.Request);
    }

    private sealed class CaptureHandler(string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }
}
