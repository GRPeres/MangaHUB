using System.IO.Compression;
using System.Net;
using MangaHub.Core.Sources;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Caching;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Tests;

public sealed class MangaDexChapterCacheTests
{
    [Fact]
    public async Task EnsureCachedAsync_DownloadsOnceThenReusesTheLocalCbz()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"mangahub-cache-{Guid.NewGuid():N}");
        var handler = new ImageHandler();
        var client = new HttpClient(handler);
        var cache = new MangaDexChapterCache(
            new FakeHttpClientFactory(client),
            Options.Create(new MangaHubOptions { MangaDexCachePath = cacheRoot }));
        var pages = new List<MangaPage>
        {
            new(0, "https://uploads.mangadex.org/data/hash/001.jpg"),
            new(1, "https://uploads.mangadex.org/data/hash/002.jpg")
        };

        try
        {
            var downloaded = await cache.EnsureCachedAsync("manga-id", "chapter-id", pages, CancellationToken.None);
            var reused = await cache.EnsureCachedAsync("manga-id", "chapter-id", pages, CancellationToken.None);

            Assert.False(downloaded.WasCached);
            Assert.True(reused.WasCached);
            Assert.Equal(2, downloaded.PageCount);
            Assert.Equal(2, handler.RequestCount);
            using var archive = ZipFile.OpenRead(Path.Combine(cacheRoot, downloaded.RelativePath));
            Assert.Equal(2, archive.Entries.Count);
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    private sealed class ImageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg") }
                }
            });
        }
    }
}
