using MangaHub.Core.Services;

namespace MangaHub.Core.Tests;

public sealed class MangaDexCacheRetentionPolicyTests
{
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

    [Theory]
    [InlineData("23", false)]
    [InlineData("24", true)]
    [InlineData("24.1", true)]
    [InlineData("25", true)]
    public void ShouldRetain_UsesTheEarliestActiveReaderChapter(string chapter, bool expected)
    {
        Assert.Equal(expected, MangaDexCacheRetentionPolicy.ShouldRetain("chapter-id", chapter, 24));
    }
}
