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

    [Fact]
    public void Compare_DoesNotTreatTenAsOne()
    {
        var ordered = new[] { "17.11", "17.2", "17.10", "17.9" }
            .OrderBy(chapter => chapter, ChapterNumberComparer.Instance)
            .ToArray();

        Assert.Equal(["17.2", "17.9", "17.10", "17.11"], ordered);
        Assert.False(ChapterNumberComparer.HasSameNumericParts("17.1", "17.10"));
    }
}
