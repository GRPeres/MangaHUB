using MangaHub.Api.Services;
using MangaHub.Core.Models;
using MangaHub.Core.Sources;

namespace MangaHub.Api.Tests;

public sealed class PlannedChapterPrefetchTests
{
    [Fact]
    public void OpeningMatchesPreferredLanguageOrderAndSkipsUnreadableReleases()
    {
        MangaSourceChapter[] chapters =
        [
            new("english-external", "0", "", 0, "en"),
            new("english-later", "2.10", "", 10, "en"),
            new("english-opening", "2.2", "", 10, "en"),
            new("portuguese-opening", "0", "", 10, "pt-br")
        ];

        Assert.Equal("english-opening", PlannedChapterPrefetch.SelectOpeningChapter(chapters, ["en", "pt-br"])?.Id);
        Assert.Equal("portuguese-opening", PlannedChapterPrefetch.SelectOpeningChapter(chapters, ["fr", "pt-br"])?.Id);
        Assert.Null(PlannedChapterPrefetch.SelectOpeningChapter(chapters, ["fr"]));
    }

    [Fact]
    public void OnlyOpeningsNeededByPlannedAccountsAreProtected()
    {
        var series = new MangaSeries { Title = "Test", Source = "mangadex-cache", ExternalId = "series" };
        series.Chapters.AddRange([
            new() { SourceId = "en-opening", ChapterNumber = "1", Language = "en", PageCount = 10 },
            new() { SourceId = "en-next", ChapterNumber = "2", Language = "en", PageCount = 10 },
            new() { SourceId = "pt-opening", ChapterNumber = "0", Language = "pt-br", PageCount = 10 }
        ]);
        var preferences = new Dictionary<string, string[]> { ["series"] = ["en,pt-br", "pt-br,en", "en"] };

        Assert.Equal(new[] { "en-opening", "pt-opening" },
            PlannedChapterPrefetch.ProtectedChapterIds(series, preferences).OrderBy(id => id));
        preferences.Clear();
        Assert.Empty(PlannedChapterPrefetch.ProtectedChapterIds(series, preferences));
    }
}
