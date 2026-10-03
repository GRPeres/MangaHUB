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
            "chapter-1", "1", null, now.AddDays(-6), now, 7);
        var expired = MangaDexCacheRetentionPolicy.ShouldRetain(
            "chapter-1", "1", null, now.AddDays(-8), now, 7);

        Assert.True(retained);
        Assert.False(expired);
    }

    [Theory]
    [InlineData("23", false)]
    [InlineData("24", true)]
    [InlineData("24.1", true)]
    [InlineData("25", true)]
    public void ShouldRetain_UsesTheEarliestActiveReaderChapter(string chapter, bool expected)
    {
        Assert.Equal(expected, MangaDexCacheRetentionPolicy.ShouldRetain("chapter-id", chapter, 24));
    }

    [Fact]
    public void FindEarliestRecordedChapter_IgnoresBlankOrInvalidProgress()
    {
        var earliest = MangaDexCacheRetentionPolicy.FindEarliestRecordedChapter(["", "not started", "24", "31.5"]);

        Assert.Equal(24, earliest);
    }

    [Fact]
    public void FindEarliestRecordedChapter_ReturnsNullWhenNoProgressWasRecorded()
    {
        var earliest = MangaDexCacheRetentionPolicy.FindEarliestRecordedChapter(["", "not started"]);

        Assert.Null(earliest);
    }
}
