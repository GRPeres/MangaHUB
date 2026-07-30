using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using MangaHub.Infrastructure.Sources;

namespace MangaHub.Api.Tests;

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string password, string storedHash) =>
        storedHash == Hash(password);
}

internal sealed class FakeSessionTokenService : ISessionTokenService
{
    public string CreateToken(Guid userId, string username) => $"token:{userId}:{username}";

    public Guid? ReadUserId(string token)
    {
        var parts = token.Split(':');
        return parts.Length >= 2 && Guid.TryParse(parts[1], out var userId) ? userId : null;
    }
}

internal sealed class FakeArchiveReader : IArchiveReader
{
    public List<string> RequestedPaths { get; } = [];

    public int CountPages(string archivePath) => 1;

    public Task<ArchivePage?> ReadPageAsync(string archivePath, int pageIndex, CancellationToken cancellationToken)
    {
        RequestedPaths.Add(archivePath);
        return Task.FromResult<ArchivePage?>(new ArchivePage($"page-{pageIndex}.jpg", "image/jpeg", [1, 2, 3]));
    }
}

internal sealed class FakeMangaDexSource : IMangaSource, IMangaDexCatalogLookup
{
    public string Name => "mangadex";
    public List<MangaSourceChapter> Chapters { get; } = [];
    public Dictionary<string, IReadOnlyList<MangaPage>> Pages { get; } = [];
    public List<MangaDexCatalogMatch> CatalogMatches { get; } = [];

    public Task<IReadOnlyList<MangaSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MangaSearchResult>>([]);

    public Task<MangaSourceSeries?> GetSeriesAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult<MangaSourceSeries?>(null);

    public Task<MangaDexCatalogMatch?> FindByMyAnimeListIdAsync(string myAnimeListId, string title, CancellationToken cancellationToken) =>
        Task.FromResult(CatalogMatches.FirstOrDefault());

    public Task<IReadOnlyList<MangaSourceChapter>> GetChaptersAsync(string seriesId, string? language, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MangaSourceChapter>>(
            string.IsNullOrWhiteSpace(language)
                ? Chapters
                : Chapters.Where(chapter => string.Equals(chapter.Language, language, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task<IReadOnlyList<MangaPage>> GetPagesAsync(string chapterId, CancellationToken cancellationToken) =>
        Task.FromResult(Pages.TryGetValue(chapterId, out var pages) ? pages : (IReadOnlyList<MangaPage>)[]);
}

internal sealed class FakeMangaDexChapterCache : IMangaDexChapterCache
{
    public List<string> CachedChapterIds { get; } = [];

    public Task<MangaDexCachedChapter> EnsureCachedAsync(
        string mangaDexId,
        string chapterId,
        IReadOnlyList<MangaPage> pages,
        CancellationToken cancellationToken,
        IProgress<ReaderPreparationProgress>? progress = null)
    {
        CachedChapterIds.Add(chapterId);
        progress?.Report(new ReaderPreparationProgress("Local chapter is ready", 100, pages.Count, pages.Count));
        return Task.FromResult(new MangaDexCachedChapter(
            Path.Combine("mangadex", mangaDexId, $"{chapterId}.cbz"),
            pages.Count,
            $"hash-{chapterId}",
            false));
    }

    public Task<MangaDexCachedChapter> ImportAsync(string mangaDexId, string chapterId, Stream content, CancellationToken cancellationToken)
    {
        CachedChapterIds.Add(chapterId);
        return Task.FromResult(new MangaDexCachedChapter(
            Path.Combine("mangadex", mangaDexId, $"{chapterId}.cbz"),
            1,
            $"hash-{chapterId}",
            false));
    }

    public Task DeleteAsync(string mangaDexId, string chapterId, CancellationToken cancellationToken)
    {
        CachedChapterIds.Remove(chapterId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeChapterTranslationEngine : IChapterTranslationEngine
{
    public bool IsEnabled => true;
    public List<ChapterTranslationRequest> Requests { get; } = [];

    public async Task<ChapterTranslationResult> TranslateAsync(
        ChapterTranslationRequest request,
        CancellationToken cancellationToken,
        IProgress<ReaderPreparationProgress>? progress = null)
    {
        Requests.Add(request);
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputArchivePath)!);
        await File.WriteAllBytesAsync(request.OutputArchivePath, [1, 2, 3], cancellationToken);
        progress?.Report(new ReaderPreparationProgress("Translated chapter ready", 97, 1, 1));
        return new ChapterTranslationResult(Path.GetFileName(request.OutputArchivePath), 1, "translated-hash");
    }
}

internal sealed class FakeMangaUpdatesClient : IMangaUpdatesClient
{
    public List<MangaUpdatesSearchResult> SearchResults { get; } = [];
    public Dictionary<string, MangaUpdatesSeriesDetails> Details { get; } = [];

    public Task<IReadOnlyList<MangaUpdatesSearchResult>> SearchSeriesAsync(string query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MangaUpdatesSearchResult>>(SearchResults);

    public Task<MangaUpdatesSeriesDetails?> GetSeriesAsync(string seriesId, CancellationToken cancellationToken) =>
        Task.FromResult(Details.TryGetValue(seriesId, out var details) ? details : null);
}

internal sealed class FakeHttpClientFactory(HttpClient? client = null) : IHttpClientFactory
{
    private readonly HttpClient httpClient = client ?? new HttpClient(new FakeImageHandler());

    public HttpClient CreateClient(string name) => httpClient;

    private sealed class FakeImageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg") }
                }
            });
    }
}
