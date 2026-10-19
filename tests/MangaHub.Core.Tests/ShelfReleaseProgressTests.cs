using MangaHub.Core.Services;

namespace MangaHub.Core.Tests;

public sealed class ShelfReleaseProgressTests
{
    [Theory]
    [InlineData("38", true, true, 38, "50", 12)]
    [InlineData("38", true, true, 45, "50", 12)]
    [InlineData("38", true, true, 50, "45", 12)]
    [InlineData("38", true, true, null, "50", 12)]
    [InlineData("38", true, false, null, "50", 12)]
    [InlineData("38", true, true, 38, "", 0)]
    [InlineData("38", false, true, 38, "", 1)]
    [InlineData("38", false, true, 38, "37", 1)]
    [InlineData("", false, true, 38, "50", 0)]
    public void CountsUnreadFromEitherSource(string current, bool read, bool hasMangaDex, int? mangaDex, string external, int expected)
    {
        Assert.Equal(expected, ShelfReleaseProgress.NewChapterCount(current, read, hasMangaDex, mangaDex, external));
    }

    [Fact]
    public void LatestChapterUsesNewestAvailableSource()
    {
        Assert.Equal(50m, ShelfReleaseProgress.LatestChapter(true, 38m, "Ch. 50"));
        Assert.Equal(51m, ShelfReleaseProgress.LatestChapter(true, 51m, "50"));
        Assert.Null(ShelfReleaseProgress.LatestChapter(false, 51m, ""));
    }
}
