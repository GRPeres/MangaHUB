using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using MangaHub.Core.Services;
using MangaHub.Core.Sources;

namespace MangaHub.Api.Tests;

public sealed class MetadataServiceTests
{
    [Fact]
    public async Task SearchAsync_PrefersMyAnimeListAndDedupesOpenLibraryTitles()
    {
        var mangaDex = new FakeMangaDexSource();
        var mangaUpdates = new FakeMangaUpdatesClient();
        var service = CreateService(
            new FakeMyAnimeListClient([
                new MetadataResult("myanimelist", "1", "Berserk", "Kentaro Miura", "", 1989, "Action", "", "manga", "currently_publishing", 376, 42, "", "1")
            ]),
            new FakeOpenLibraryClient([
                new OpenLibrarySearchResult("/works/OL1W", "Berserk", "Kentaro Miura", "", 1989, "Comics", ""),
                new OpenLibrarySearchResult("/works/OL2W", "Berserk Deluxe", "Kentaro Miura", "", 2019, "Comics", "")
            ]),
            mangaDex,
            mangaUpdates);

        var results = await service.SearchAsync("berserk", includeOpenLibrary: true, CancellationToken.None);

        Assert.Equal(["myanimelist", "openlibrary"], results.Select(x => x.Source));
        Assert.Equal(["Berserk", "Berserk Deluxe"], results.Select(x => x.Title));
    }

    [Fact]
    public async Task SearchAsync_PreservesAlternateTitlesFromEverySource()
    {
        var service = CreateService(
            new FakeMyAnimeListClient([
                new MetadataResult("myanimelist", "1", "Kaguya-sama wa Kokurasetai", "Aka Akasaka", "", 2015, "Romance", "", "manga", "finished", 281, 28, "", "1", ["Kaguya-sama: Love Is War"])
            ]),
            new FakeOpenLibraryClient([
                new OpenLibrarySearchResult("/works/OL1W", "Oshi no Ko", "Aka Akasaka", "", 2020, "Drama", "", ["My Star"])
            ]),
            new FakeMangaDexSource(),
            new FakeMangaUpdatesClient());

        var results = await service.SearchAsync("kaguya", includeOpenLibrary: true, CancellationToken.None);

        Assert.Equal(["Kaguya-sama: Love Is War"], results[0].AlternateTitles);
        Assert.Equal(["My Star"], results[1].AlternateTitles);
    }

    [Fact]
    public async Task SearchAsync_FallsBackToMangaDexBeforeMangaUpdatesAndOpenLibrary()
    {
        var mangaDex = new FakeMangaDexSource();
        mangaDex.SearchResults.Add(new MangaSearchResult(
            "mangadex-id", "MangaDex Result", "A description", "https://cover.example/cover.jpg", "ongoing", "mangadex", ["Alternate name"]));
        var service = CreateService(
            new FakeMyAnimeListClient([]),
            new FakeOpenLibraryClient([new OpenLibrarySearchResult("/works/OL1W", "OpenLibrary Result", "", "", null, "", "")]),
            mangaDex,
            new FakeMangaUpdatesClient());

        var results = await service.SearchAsync("manga", includeOpenLibrary: true, CancellationToken.None);
        var result = results[0];

        Assert.Equal("mangadex", result.Source);
        Assert.Equal("mangadex-id", result.SourceId);
        Assert.Empty(result.CoverUrl);
        Assert.Equal(["Alternate name"], result.AlternateTitles);
        Assert.Equal("openlibrary", results[1].Source);
    }

    [Fact]
    public async Task SearchAsync_FallsBackToMangaUpdatesWhenMangaDexHasNoResults()
    {
        var mangaUpdates = new FakeMangaUpdatesClient();
        mangaUpdates.SearchResults.Add(new MangaUpdatesSearchResult("mangaupdates-id", "MangaUpdates Result", "Manhwa", 2024, ["Other title"]));
        var service = CreateService(
            new FakeMyAnimeListClient([]),
            new FakeOpenLibraryClient([]),
            new FakeMangaDexSource(),
            mangaUpdates);

        var result = Assert.Single(await service.SearchAsync("manga", includeOpenLibrary: false, CancellationToken.None));

        Assert.Equal("mangaupdates", result.Source);
        Assert.Equal("mangaupdates-id", result.SourceId);
        Assert.Equal("Manhwa", result.Category);
        Assert.Equal(["Other title"], result.AlternateTitles);
    }

    [Fact]
    public async Task FindMangaDexMatchAsync_ReturnsTheMangaDexMatchForMalMetadata()
    {
        var mangaDex = new FakeMangaDexSource();
        mangaDex.CatalogMatches.Add(new MangaDexCatalogMatch("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Berserk"));
        var service = CreateService(new FakeMyAnimeListClient([]), new FakeOpenLibraryClient([]), mangaDex, new FakeMangaUpdatesClient());

        var match = await service.FindMangaDexMatchAsync("2", "Berserk", CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", match!.Id);
    }

    [Fact]
    public async Task GetMangaDexMetadataAsync_ReturnsDirectSeriesMetadata()
    {
        var mangaDex = new FakeMangaDexSource
        {
            Series = new MangaSourceSeries(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Berserk", "Dark fantasy", "https://cover.example/berserk.jpg", "ongoing", "mangadex", 1989, "Action", ["Berserk: The Prototype"])
        };
        var service = CreateService(new FakeMyAnimeListClient([]), new FakeOpenLibraryClient([]), mangaDex, new FakeMangaUpdatesClient());

        var result = await service.GetMangaDexMetadataAsync(mangaDex.Series.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("mangadex", result!.Source);
        Assert.Equal("Berserk", result.Title);
        Assert.Equal("Dark fantasy", result.Description);
        Assert.Empty(result.CoverUrl);
        Assert.Equal(1989, result.FirstPublishYear);
    }

    [Fact]
    public async Task GetMangaUpdatesMetadataAsync_EnrichesDirectSeriesWithSearchData()
    {
        var mangaUpdates = new FakeMangaUpdatesClient();
        mangaUpdates.Details["123"] = new MangaUpdatesSeriesDetails("123", "Berserk", 382, "Ongoing", false);
        mangaUpdates.SearchResults.Add(new MangaUpdatesSearchResult("123", "Berserk", "Manga", 1989, ["Berserk: The Prototype"]));
        var service = CreateService(new FakeMyAnimeListClient([]), new FakeOpenLibraryClient([]), new FakeMangaDexSource(), mangaUpdates);

        var result = await service.GetMangaUpdatesMetadataAsync("123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("mangaupdates", result!.Source);
        Assert.Equal("Manga", result.Category);
        Assert.Equal("Ongoing", result.PublishingStatus);
        Assert.Equal(1989, result.FirstPublishYear);
    }

    private static MetadataService CreateService(
        IMyAnimeListClient myAnimeList,
        IOpenLibraryClient openLibrary,
        FakeMangaDexSource mangaDex,
        FakeMangaUpdatesClient mangaUpdates) =>
        new(
            myAnimeList,
            openLibrary,
            [mangaDex],
            mangaUpdates,
            new MangaDexCatalogMatchService(mangaDex),
            new MangaUpdatesCatalogMatchService(mangaUpdates));

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
