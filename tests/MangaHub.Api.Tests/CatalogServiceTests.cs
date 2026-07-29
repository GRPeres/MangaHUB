using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using MangaHub.Core.Services;

namespace MangaHub.Api.Tests;

public sealed class CatalogServiceTests
{
    [Fact]
    public async Task CreateAsync_UsesOpenLibraryDetailsWhenRequestLeavesFieldsBlank()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeOpenLibrary(new OpenLibraryWorkDetails("Manga", "Fetched summary")));

        var created = await service.CreateAsync(Guid.NewGuid(), Request(openLibraryKey: "/works/OL1W"), CancellationToken.None);

        Assert.Equal("Manga", created.Category);
        Assert.Equal("Fetched summary", created.Description);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesMetadataAndExtractsMangaDexId()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeOpenLibrary(null));
        var created = await service.CreateAsync(Guid.NewGuid(), Request(title: "Old"), CancellationToken.None);

        var updated = await service.UpdateAsync(Guid.NewGuid(), created.Id, Request(
            title: "New",
            mangaDexId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("New", updated.Title);
        Assert.Equal("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", updated.MangaDexId);
    }

    [Fact]
    public async Task CreateAsync_MyAnimeListMetadataFindsMatchingMangaDexEntry()
    {
        await using var db = TestDb.Create();
        var mangaDex = new FakeMangaDexSource();
        mangaDex.CatalogMatches.Add(new MangaDexCatalogMatch("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Berserk"));
        var service = CreateService(db, new FakeOpenLibrary(null), mangaDex);

        var created = await service.CreateAsync(
            Guid.NewGuid(),
            Request(metadataSource: "myanimelist", myAnimeListId: "2"),
            CancellationToken.None);

        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", created.MangaDexId);
    }

    [Fact]
    public async Task CreateAsync_ManualMangaDexIdTakesPrecedenceOverAutomaticLookup()
    {
        await using var db = TestDb.Create();
        var mangaDex = new FakeMangaDexSource();
        mangaDex.CatalogMatches.Add(new MangaDexCatalogMatch("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Berserk"));
        var service = CreateService(db, new FakeOpenLibrary(null), mangaDex);
        const string manualId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

        var created = await service.CreateAsync(
            Guid.NewGuid(),
            Request(metadataSource: "myanimelist", myAnimeListId: "2", mangaDexId: manualId),
            CancellationToken.None);

        Assert.Equal(manualId, created.MangaDexId);
    }

    [Fact]
    public async Task CreateAsync_AcceptsALegacyMangaDexUrlInTheIdField()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeOpenLibrary(null));

        var created = await service.CreateAsync(
            Guid.NewGuid(),
            Request(mangaDexId: "https://mangadex.org/title/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/berserk"),
            CancellationToken.None);

        Assert.Equal("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", created.MangaDexId);
    }

    [Fact]
    public async Task CreateAsync_AutomaticallyBindsAnExactMangaUpdatesTitleMatch()
    {
        await using var db = TestDb.Create();
        var mangaUpdates = new FakeMangaUpdatesClient();
        mangaUpdates.SearchResults.Add(new MangaUpdatesSearchResult("123", "Berserk", "Manga", 1989, []));
        var service = CreateService(db, new FakeOpenLibrary(null), mangaUpdates: mangaUpdates);

        var created = await service.CreateAsync(Guid.NewGuid(), Request(), CancellationToken.None);

        Assert.Equal("123", created.MangaUpdatesId);
    }

    [Fact]
    public async Task CreateAsync_StoresTheDedicatedFallbackReaderUrl()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeOpenLibrary(null));

        var created = await service.CreateAsync(
            Guid.NewGuid(),
            Request(fallbackReaderUrl: "https://reader.example.com/berserk"),
            CancellationToken.None);

        Assert.Equal("", created.MangaDexId);
        Assert.Equal("https://reader.example.com/berserk", created.FallbackReaderUrl);
    }

    private static CatalogService CreateService(
        MangaHub.Infrastructure.Data.MangaHubDbContext db,
        IOpenLibraryClient openLibrary,
        FakeMangaDexSource? mangaDex = null,
        FakeMangaUpdatesClient? mangaUpdates = null) =>
        new(
            new CatalogRepository(db),
            openLibrary,
            new MangaDexCatalogMatchService(mangaDex ?? new FakeMangaDexSource()),
            new MangaUpdatesCatalogMatchService(mangaUpdates ?? new FakeMangaUpdatesClient()));

    private static MangaEntryRequest Request(
        string title = "Berserk",
        string openLibraryKey = "",
        string mangaDexId = "",
        string fallbackReaderUrl = "",
        string metadataSource = "manual",
        string myAnimeListId = "") =>
        new(
            Title: title,
            Authors: "Kentaro Miura",
            Category: "",
            Description: "",
            CoverUrl: "",
            OpenLibraryKey: openLibraryKey,
            FirstPublishYear: null,
            ReadingStatus: "planned",
            MangaDexId: mangaDexId,
            LocalSeriesId: null,
            Notes: "",
            MetadataSource: metadataSource,
            MyAnimeListId: myAnimeListId,
            MediaType: "",
            PublishingStatus: "",
            ChapterCount: null,
            VolumeCount: null,
            FallbackReaderUrl: fallbackReaderUrl);

    private sealed class FakeOpenLibrary(OpenLibraryWorkDetails? details) : IOpenLibraryClient
    {
        public Task<IReadOnlyList<OpenLibrarySearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OpenLibrarySearchResult>>([]);

        public Task<OpenLibraryWorkDetails?> GetWorkAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(details);
    }
}
