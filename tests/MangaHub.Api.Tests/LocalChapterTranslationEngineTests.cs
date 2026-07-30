using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Translation;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Tests;

public sealed class LocalChapterTranslationEngineTests
{
    [Fact]
    public async Task TranslateAsync_SendsPageToMangaTranslatorAndBuildsTranslatedArchive()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mangahub-engine-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "source.cbz");
            var outputPath = Path.Combine(root, "translated.cbz");
            await CreateSourceArchiveAsync(sourcePath);
            var handler = new MangaTranslatorHandler();
            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri("http://manga-translator:5003/")
            };
            var engine = new LocalChapterTranslationEngine(
                new TestHttpClientFactory(client),
                Options.Create(new MangaHubOptions
                {
                    Translation = new ChapterTranslationOptions
                    {
                        Enabled = true,
                        Translator = "nllb"
                    }
                }));

            var result = await engine.TranslateAsync(
                new ChapterTranslationRequest(sourcePath, outputPath, "ja", "pt-br"),
                CancellationToken.None);

            Assert.Equal(1, result.PageCount);
            Assert.True(File.Exists(outputPath));
            Assert.Contains("\"translator\":\"nllb\"", handler.TranslatorConfig);
            Assert.Contains("\"target_lang\":\"PTB\"", handler.TranslatorConfig);
            using var archive = ZipFile.OpenRead(outputPath);
            Assert.Equal("0001.png", Assert.Single(archive.Entries).FullName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CreateSourceArchiveAsync(string path)
    {
        await using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("001.jpg");
        await using var page = entry.Open();
        await page.WriteAsync(Enumerable.Range(0, 256).Select(value => (byte)value).ToArray());
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class MangaTranslatorHandler : HttpMessageHandler
    {
        public string TranslatorConfig { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/queue-size", StringComparison.Ordinal) == true)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("0")
                };
            }

            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            var configPart = multipart.First(part =>
                string.Equals(
                    part.Headers.ContentDisposition?.Name?.Trim('"'),
                    "config",
                    StringComparison.Ordinal));
            TranslatorConfig = await configPart.ReadAsStringAsync(cancellationToken);
            var content = new ByteArrayContent(Enumerable.Repeat((byte)42, 256).ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
}
