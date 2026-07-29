using System.Net;
using MangaHub.Infrastructure.Sources;

namespace MangaHub.Api.Tests;

public sealed class MangaUpdatesClientTests
{
    [Fact]
    public async Task SearchSeriesAsync_ParsesNumericSeriesIdsAndStringYears()
    {
        var client = CreateClient("""
            {"results":[{"record":{"series_id":51239621230,"title":"Berserk","type":"Manga","year":"1989","associated":[]}}]}
            """);

        var results = await client.SearchSeriesAsync("Berserk", CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("51239621230", result.Id);
        Assert.Equal(1989, result.Year);
    }

    [Fact]
    public async Task GetSeriesAsync_ParsesNumericSeriesIdAndStringChapter()
    {
        var client = CreateClient("""
            {"series_id":51239621230,"title":"Berserk","latest_chapter":"382","status":"Ongoing","completed":false}
            """);

        var result = await client.GetSeriesAsync("51239621230", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("51239621230", result.Id);
        Assert.Equal(382m, result.LatestChapter);
    }

    private static MangaUpdatesClient CreateClient(string responseBody) => new(new HttpClient(new ResponseHandler(responseBody))
    {
        BaseAddress = new Uri("https://api.mangaupdates.com/")
    });

    private sealed class ResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
    }
}
