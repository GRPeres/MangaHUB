using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using MangaHub.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
    public async Task CreateAsync_QueuesBackgroundIdentityEnrichment()
    {
        await using var db = TestDb.Create();
        var mangaDex = new FakeMangaDexSource();
        mangaDex.CatalogMatches.Add(new MangaDexCatalogMatch("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Berserk"));
        var service = CreateService(db, new FakeOpenLibrary(null), mangaDex);

        var created = await service.CreateAsync(
            Guid.NewGuid(),
            Request(metadataSource: "myanimelist", myAnimeListId: "2"),
            CancellationToken.None);

        Assert.Equal("", created.MangaDexId);
        Assert.Contains(await db.MaintenanceJobs.ToListAsync(), job => job.Type == CatalogIdentityEnrichmentService.JobType && job.Status == "queued");
    }

    [Fact]
    public async Task CreateAsync_CoalescesConcurrentIdentityEnrichmentRequests()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeOpenLibrary(null));

        await service.CreateAsync(Guid.NewGuid(), Request(title: "Berserk"), CancellationToken.None);
        await service.CreateAsync(Guid.NewGuid(), Request(title: "Vagabond"), CancellationToken.None);

        Assert.Single(await db.MaintenanceJobs.Where(job => job.Type == CatalogIdentityEnrichmentService.JobType).ToListAsync());
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
    public async Task IdentityEnrichment_FillsMissingIdsWithoutOverwritingManualValues()
    {
        await using var db = TestDb.Create();
        var mangaUpdates = new FakeMangaUpdatesClient();
        mangaUpdates.SearchResults.Add(new MangaUpdatesSearchResult("123", "Berserk", "Manga", 1989, []));
        var mangaDex = new FakeMangaDexSource();
        mangaDex.CatalogMatches.Add(new MangaDexCatalogMatch("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Berserk"));
        var service = CreateService(db, new FakeOpenLibrary(null), mangaDex, mangaUpdates);

        var created = await service.CreateAsync(Guid.NewGuid(), Request(metadataSource: "myanimelist", myAnimeListId: "2"), CancellationToken.None);
        var enrichment = CreateEnrichment(db, mangaDex, mangaUpdates);
        await enrichment.RunAsync(CancellationToken.None);

        var enriched = await db.MangaEntries.FindAsync([created.Id]);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", enriched!.MangaDexId);
        Assert.Equal("123", enriched.MangaUpdatesId);

        enriched.MangaDexId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        enriched.MangaUpdatesId = "456";
        enriched.MangaDexLastMatchAttemptAt = null;
        enriched.MangaUpdatesLastMatchAttemptAt = null;
        await db.SaveChangesAsync();

        await enrichment.RunAsync(CancellationToken.None);

        Assert.Equal("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", enriched.MangaDexId);
        Assert.Equal("456", enriched.MangaUpdatesId);
    }

    [Fact]
    public async Task IdentityEnrichment_UsesAnExactMangaDexTitleWhenMalIdIsMissing()
    {
        await using var db = TestDb.Create();
        var mangaDex = new FakeMangaDexSource();
        mangaDex.SearchResults.Add(new MangaSearchResult(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Berserk", "", "", "ongoing", "mangadex"));
        var mangaUpdates = new FakeMangaUpdatesClient();
        var service = CreateService(db, new FakeOpenLibrary(null), mangaDex, mangaUpdates);

        var created = await service.CreateAsync(Guid.NewGuid(), Request(), CancellationToken.None);
        await CreateEnrichment(db, mangaDex, mangaUpdates).RunAsync(CancellationToken.None);

        var enriched = await db.MangaEntries.FindAsync([created.Id]);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", enriched!.MangaDexId);
    }

    [Fact]
    public async Task CreateAsync_RejectsAnExistingMangaDexId()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeOpenLibrary(null));
        await service.CreateAsync(Guid.NewGuid(), Request(title: "First", mangaDexId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CatalogDuplicateIdentityException>(() =>
            service.CreateAsync(Guid.NewGuid(), Request(title: "Second", mangaDexId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), CancellationToken.None));

        Assert.Contains("MangaDex", exception.Message);
        Assert.Contains("First", exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_RejectsAnotherEntriesMangaUpdatesId()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeOpenLibrary(null));
        var first = await service.CreateAsync(Guid.NewGuid(), Request(title: "First", mangaDexId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), CancellationToken.None);
        var second = await service.CreateAsync(Guid.NewGuid(), Request(title: "Second", mangaDexId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), CancellationToken.None);
        var firstEntity = await db.MangaEntries.FindAsync([first.Id]);
        firstEntity!.MangaUpdatesId = "123";
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CatalogDuplicateIdentityException>(() =>
            service.UpdateAsync(Guid.NewGuid(), second.Id, Request(title: "Second", mangaDexId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", mangaUpdatesId: "123"), CancellationToken.None));

        Assert.Contains("MangaUpdates", exception.Message);
        Assert.Contains("First", exception.Message);
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

    [Fact]
    public async Task CreateAsync_StoresTheChosenReaderPreference()
    {
        await using var db = TestDb.Create();
        var service = CreateService(db, new FakeOpenLibrary(null));

        var created = await service.CreateAsync(
            Guid.NewGuid(),
            Request(
                fallbackReaderUrl: "https://reader.example.com/berserk",
                readerPreference: "hybrid"),
            CancellationToken.None);

        Assert.Equal("hybrid", created.ReaderPreference);
    }

    private static CatalogService CreateService(
        MangaHub.Infrastructure.Data.MangaHubDbContext db,
        IOpenLibraryClient openLibrary,
        FakeMangaDexSource? mangaDex = null,
        FakeMangaUpdatesClient? mangaUpdates = null)
    {
        var resolvedMangaDex = mangaDex ?? new FakeMangaDexSource();
        var resolvedMangaUpdates = mangaUpdates ?? new FakeMangaUpdatesClient();
        var catalog = new CatalogRepository(db);
        return new CatalogService(catalog, openLibrary, CreateEnrichment(db, resolvedMangaDex, resolvedMangaUpdates));
    }

    private static CatalogIdentityEnrichmentService CreateEnrichment(
        MangaHub.Infrastructure.Data.MangaHubDbContext db,
        FakeMangaDexSource mangaDex,
        FakeMangaUpdatesClient mangaUpdates) =>
        new(
            db,
            new CatalogRepository(db),
            new MangaDexCatalogMatchService(mangaDex),
            new MangaDexTitleMatchService([mangaDex]),
            new MangaUpdatesCatalogMatchService(mangaUpdates),
            Options.Create(new MangaHubOptions()),
            NullLogger<CatalogIdentityEnrichmentService>.Instance);

    private static MangaEntryRequest Request(
        string title = "Berserk",
        string openLibraryKey = "",
        string mangaDexId = "",
        string fallbackReaderUrl = "",
        string readerPreference = "mangahub",
        string metadataSource = "manual",
        string myAnimeListId = "",
        string mangaUpdatesId = "") =>
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
            FallbackReaderUrl: fallbackReaderUrl,
            ReaderPreference: readerPreference,
            MangaUpdatesId: mangaUpdatesId);

    private sealed class FakeOpenLibrary(OpenLibraryWorkDetails? details) : IOpenLibraryClient
    {
        public Task<IReadOnlyList<OpenLibrarySearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OpenLibrarySearchResult>>([]);

        public Task<OpenLibraryWorkDetails?> GetWorkAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(details);
    }
}
