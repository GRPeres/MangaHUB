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

    [Fact]
    public async Task ArchiveAsync_MovesDataSaverChapterOutOfTheSyncedCacheAndCanRestoreIt()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"mangahub-cache-{Guid.NewGuid():N}");
        var handler = new ImageHandler();
        var cache = new MangaDexChapterCache(
            new FakeHttpClientFactory(new HttpClient(handler)),
            Options.Create(new MangaHubOptions { MangaDexCachePath = cacheRoot }));
        var pages = new List<MangaPage>
        {
            new(0, "https://uploads.mangadex.org/data-saver/hash/001.jpg")
        };

        try
        {
            var cached = await cache.EnsureCachedAsync("manga-id", "chapter-id", pages, CancellationToken.None, imageQuality: "data-saver");
            var activePath = Path.Combine(cacheRoot, cached.RelativePath);
            var archivePath = Path.Combine(cacheRoot, "archive", "mangadex", "data-saver", "manga-id", "chapter-id.cbz");

            Assert.Equal(Path.Combine("mangadex", "data-saver", "manga-id", "chapter-id.cbz"), cached.RelativePath);
            Assert.True(File.Exists(activePath));

            Assert.True(await cache.ArchiveAsync("manga-id", "chapter-id", CancellationToken.None, "data-saver"));
            Assert.False(File.Exists(activePath));
            Assert.True(File.Exists(archivePath));
            Assert.Contains("/archive", await File.ReadAllTextAsync(Path.Combine(cacheRoot, ".stignore")));

            var restored = await cache.EnsureCachedAsync("manga-id", "chapter-id", [], CancellationToken.None, imageQuality: "data-saver");
            Assert.True(restored.WasCached);
            Assert.Equal(1, handler.RequestCount);
            Assert.True(File.Exists(activePath));
            Assert.False(File.Exists(archivePath));
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
