using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using MangaHub.Core.Services;

namespace MangaHub.Api.Tests;

public sealed class MetadataServiceTests
{
    [Fact]
    public async Task SearchAsync_PrefersMyAnimeListAndDedupesOpenLibraryTitles()
    {
        var service = new MetadataService(
            new FakeMyAnimeListClient([
                new MetadataResult("myanimelist", "1", "Berserk", "Kentaro Miura", "", 1989, "Action", "", "manga", "currently_publishing", 376, 42, "", "1")
            ]),
            new FakeOpenLibraryClient([
                new OpenLibrarySearchResult("/works/OL1W", "Berserk", "Kentaro Miura", "", 1989, "Comics", ""),
                new OpenLibrarySearchResult("/works/OL2W", "Berserk Deluxe", "Kentaro Miura", "", 2019, "Comics", "")
            ]),
            new MangaDexCatalogMatchService(new FakeMangaDexSource()),
            new MangaUpdatesCatalogMatchService(new FakeMangaUpdatesClient()));

        var results = await service.SearchAsync("berserk", includeOpenLibrary: true, CancellationToken.None);

        Assert.Equal(["myanimelist", "openlibrary"], results.Select(x => x.Source));
        Assert.Equal(["Berserk", "Berserk Deluxe"], results.Select(x => x.Title));
    }

    [Fact]
    public async Task FindMangaDexMatchAsync_ReturnsTheMangaDexMatchForMalMetadata()
    {
        var mangaDex = new FakeMangaDexSource();
        mangaDex.CatalogMatches.Add(new MangaDexCatalogMatch("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Berserk"));
        var service = new MetadataService(
            new FakeMyAnimeListClient([]),
            new FakeOpenLibraryClient([]),
            new MangaDexCatalogMatchService(mangaDex),
            new MangaUpdatesCatalogMatchService(new FakeMangaUpdatesClient()));

        var match = await service.FindMangaDexMatchAsync("2", "Berserk", CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", match!.Id);
    }

    private sealed class FakeMyAnimeListClient(IReadOnlyList<MetadataResult> results) : IMyAnimeListClient
    {
        public Task<IReadOnlyList<MetadataResult>> SearchMangaAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult(results);
    }

    private sealed class FakeOpenLibraryClient(IReadOnlyList<OpenLibrarySearchResult> results) : IOpenLibraryClient
    {
        public Task<IReadOnlyList<OpenLibrarySearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult(results);

        public Task<OpenLibraryWorkDetails?> GetWorkAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult<OpenLibraryWorkDetails?>(null);
    }
}
