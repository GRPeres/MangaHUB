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
        var service = new CatalogService(new CatalogRepository(db), new FakeOpenLibrary(new OpenLibraryWorkDetails("Manga", "Fetched summary")));

        var created = await service.CreateAsync(Guid.NewGuid(), Request(openLibraryKey: "/works/OL1W"), CancellationToken.None);

        Assert.Equal("Manga", created.Category);
        Assert.Equal("Fetched summary", created.Description);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesMetadataAndExtractsMangaDexId()
    {
        await using var db = TestDb.Create();
        var service = new CatalogService(new CatalogRepository(db), new FakeOpenLibrary(null));
        var created = await service.CreateAsync(Guid.NewGuid(), Request(title: "Old"), CancellationToken.None);

        var updated = await service.UpdateAsync(Guid.NewGuid(), created.Id, Request(
            title: "New",
            mangaDexUrl: "https://mangadex.org/title/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/new"), CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("New", updated.Title);
        Assert.Equal("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", updated.MangaDexId);
    }

    private static MangaEntryRequest Request(
        string title = "Berserk",
        string openLibraryKey = "",
        string mangaDexUrl = "") =>
        new(
            Title: title,
            Authors: "Kentaro Miura",
            Category: "",
            Description: "",
            CoverUrl: "",
            OpenLibraryKey: openLibraryKey,
            FirstPublishYear: null,
            ReadingStatus: "planned",
            MangaDexUrl: mangaDexUrl,
            LocalSeriesId: null,
            Notes: "",
            MetadataSource: "manual",
            MyAnimeListId: "",
            MediaType: "",
            PublishingStatus: "",
            ChapterCount: null,
            VolumeCount: null);

    private sealed class FakeOpenLibrary(OpenLibraryWorkDetails? details) : IOpenLibraryClient
    {
        public Task<IReadOnlyList<OpenLibrarySearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OpenLibrarySearchResult>>([]);

        public Task<OpenLibraryWorkDetails?> GetWorkAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(details);
    }
}
