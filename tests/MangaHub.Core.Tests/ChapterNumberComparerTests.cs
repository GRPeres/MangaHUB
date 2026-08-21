using MangaHub.Core.Services;

namespace MangaHub.Core.Tests;

public sealed class ChapterNumberComparerTests
{
    [Fact]
    public void Compare_OrdersMultipartChapterSuffixesNaturally()
    {
        var ordered = new[] { "17.2", "17.1.5", "17.1" }
            .OrderBy(chapter => chapter, ChapterNumberComparer.Instance)
            .ToArray();

        Assert.Equal(["17.1", "17.1.5", "17.2"], ordered);
    }
}
