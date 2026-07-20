using System.IO.Compression;
using System.Net;
using MangaHub.Core.Services;
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

    [Fact]
    public async Task EnsureCachedAsync_ReportsActualPageDownloadProgress()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"mangahub-cache-{Guid.NewGuid():N}");
        var cache = new MangaDexChapterCache(
            new FakeHttpClientFactory(new HttpClient(new ImageHandler())),
            Options.Create(new MangaHubOptions { MangaDexCachePath = cacheRoot }));
        var progress = new RecordingProgress();
        var pages = new List<MangaPage>
        {
            new(0, "https://uploads.mangadex.org/data/hash/001.jpg"),
            new(1, "https://uploads.mangadex.org/data/hash/002.jpg")
        };

        try
        {
            await cache.EnsureCachedAsync("manga-id", "chapter-id", pages, CancellationToken.None, progress);

            Assert.Contains(progress.Values, value => value.Stage == "Downloaded page 1 of 2" && value.CompletedPages == 1 && value.TotalPages == 2);
            Assert.Contains(progress.Values, value => value.Stage == "Downloaded page 2 of 2" && value.Progress == 90);
            Assert.Equal("Local chapter is ready", progress.Values.Last().Stage);
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

    private sealed class RecordingProgress : IProgress<ReaderPreparationProgress>
    {
        public List<ReaderPreparationProgress> Values { get; } = [];

        public void Report(ReaderPreparationProgress value) => Values.Add(value);
    }
}
