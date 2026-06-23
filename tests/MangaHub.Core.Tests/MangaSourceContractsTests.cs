using MangaHub.Core.Sources;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;

namespace MangaHub.Core.Tests;

public sealed class MangaSourceContractsTests
{
    [Fact]
    public void SearchResult_CarriesSourceName()
    {
        var result = new MangaSearchResult("id", "Title", "", "", "unknown", "local");

        Assert.Equal("local", result.Source);
    }

    [Fact]
    public void MangaSourceRecords_PreserveIdentifiersAndPagingData()
    {
        var series = new MangaSourceSeries("mal-1", "Berserk", "Dark fantasy", "cover.jpg", "publishing", "myanimelist");
        var chapter = new MangaSourceChapter("chapter-1", "376", "The Black Swordsman", 24);
        var page = new MangaPage(2, "https://example.test/page.jpg");

        Assert.Equal("mal-1", series.Id);
        Assert.Equal("myanimelist", series.Source);
        Assert.Equal("376", chapter.Number);
        Assert.Equal(24, chapter.PageCount);
        Assert.Equal(2, page.Index);
    }

    [Fact]
    public void MangaEntryRequest_DefaultsOptionalMetadata()
    {
        var request = new MangaEntryRequest(
            "Berserk",
            "Kentaro Miura",
            "Seinen",
            "",
            "",
            "",
            1989,
            "planned",
            "",
            null,
            "");

        Assert.Equal("Berserk", request.Title);
        Assert.Equal("Kentaro Miura", request.Authors);
        Assert.Equal("Seinen", request.Category);
        Assert.Equal("", request.MetadataSource);
        Assert.Equal("", request.MyAnimeListId);
        Assert.Null(request.LocalSeriesId);
    }

    [Fact]
    public void AddToShelfRequest_CarriesPersonalShelfFields()
    {
        var mangaId = Guid.NewGuid();
        var request = new AddToShelfRequest(mangaId, "done", "100", 5, "favorite", "summary", "notes");

        Assert.Equal(mangaId, request.MangaEntryId);
        Assert.Equal("done", request.ReadingStatus);
        Assert.Equal("100", request.CurrentChapter);
        Assert.Equal(5, request.Score);
        Assert.Equal("favorite", request.Category);
        Assert.Equal("summary", request.Summary);
        Assert.Equal("notes", request.Notes);
    }

    [Fact]
    public void DomainModels_HaveStableDefaultsForNewRows()
    {
        var user = new MangaUser { Username = "delta", PasswordHash = "hash" };
        var series = new MangaSeries { Title = "Berserk", Source = "local", ExternalId = "berserk" };
        var shelf = new UserMangaEntry();

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("user", user.Role);
        Assert.Equal("unknown", series.Status);
        Assert.Empty(series.Chapters);
        Assert.Equal("planned", shelf.ReadingStatus);
        Assert.Null(shelf.Score);
    }
}
