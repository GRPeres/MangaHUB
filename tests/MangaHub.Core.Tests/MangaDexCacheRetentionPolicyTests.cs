using MangaHub.Core.Services;

namespace MangaHub.Core.Tests;

public sealed class MangaDexCacheRetentionPolicyTests
{
    [Theory]
    [InlineData(false, 3, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, true)]
    public void ContinuationRequiresABatchLimitAndActualArchiveProgress(bool batchLimitReached, int archivedCount, bool expected)
    {
        Assert.Equal(expected, MangaDexCacheRetentionPolicy.ShouldQueueContinuation(batchLimitReached, archivedCount));
    }

    [Fact]
    public void ShouldRetain_KeepsManualImportsRegardlessOfReaderProgress()
    {
        Assert.True(MangaDexCacheRetentionPolicy.ShouldRetain("manual-chapter", "1", null));
    }

    [Fact]
    public void ShouldRetain_RemovesAutomaticCacheWithoutActiveReaders()
    {
        Assert.False(MangaDexCacheRetentionPolicy.ShouldRetain("chapter-1", "1", null));
    }

    [Fact]
    public void ShouldRetain_KeepsRecentlyAccessedChapterDuringGracePeriod()
    {
        var now = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

        var retained = MangaDexCacheRetentionPolicy.ShouldRetain(
            "chapter-1", "1", Array.Empty<MangaDexCacheRetentionPolicy.ReaderProgress>(), now.AddDays(-6), now, 7);
        var expired = MangaDexCacheRetentionPolicy.ShouldRetain(
            "chapter-1", "1", Array.Empty<MangaDexCacheRetentionPolicy.ReaderProgress>(), now.AddDays(-8), now, 7);

        Assert.True(retained);
        Assert.False(expired);
    }

    [Theory]
    [InlineData("23", false)]
    [InlineData("24", true)]
    [InlineData("24.1", true)]
    [InlineData("25", true)]
    public void ShouldRetain_KeepsTheCurrentChapterWhenAReaderHasNotFinishedIt(string chapter, bool expected)
    {
        var progress = new[] { new MangaDexCacheRetentionPolicy.ReaderProgress("24", IncludeCurrentChapter: true) };

        Assert.Equal(expected, MangaDexCacheRetentionPolicy.ShouldRetain("chapter-id", chapter, progress, null, DateTimeOffset.UtcNow, 0));
    }

    [Theory]
    [InlineData("24", false)]
    [InlineData("24.1", true)]
    [InlineData("25", true)]
    public void ShouldRetain_ArchivesACompletedReadersCurrentChapterButKeepsNewerChapters(string chapter, bool expected)
    {
        var progress = new[] { new MangaDexCacheRetentionPolicy.ReaderProgress("24", IncludeCurrentChapter: false) };

        Assert.Equal(expected, MangaDexCacheRetentionPolicy.ShouldRetain("chapter-id", chapter, progress, null, DateTimeOffset.UtcNow, 0));
    }

    [Theory]
    [InlineData("17.1", false)]
    [InlineData("17.1.5", true)]
    [InlineData("17.2", true)]
    public void ShouldRetain_OrdersNestedChapterSubdivisionsCorrectly(string chapter, bool expected)
    {
        var progress = new[] { new MangaDexCacheRetentionPolicy.ReaderProgress("17.1", IncludeCurrentChapter: false) };

        Assert.Equal(expected, MangaDexCacheRetentionPolicy.ShouldRetain("chapter-id", chapter, progress, null, DateTimeOffset.UtcNow, 0));
    }

    [Fact]
    public void ShouldRetain_HonorsTheMostConservativeOfMultipleReaders()
    {
        var progress = new[]
        {
            new MangaDexCacheRetentionPolicy.ReaderProgress("24", IncludeCurrentChapter: false),
            new MangaDexCacheRetentionPolicy.ReaderProgress("25", IncludeCurrentChapter: true)
        };

        Assert.True(MangaDexCacheRetentionPolicy.ShouldRetain("chapter-id", "24.1", progress, null, DateTimeOffset.UtcNow, 0));
        Assert.True(MangaDexCacheRetentionPolicy.ShouldRetain("chapter-id", "25", progress, null, DateTimeOffset.UtcNow, 0));
    }
}
