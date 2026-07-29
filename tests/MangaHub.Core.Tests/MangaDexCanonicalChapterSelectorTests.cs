using MangaHub.Core.Sources;

namespace MangaHub.Core.Tests;

public sealed class MangaDexCanonicalChapterSelectorTests
{
    [Fact]
    public void SelectOnePerLogicalChapter_KeepsOneDeterministicSourceAcrossLanguages()
    {
        var chapters = new[]
        {
            new MangaSourceChapter("z-source", "42", "", 0, "pt-br"),
            new MangaSourceChapter("a-source", "42", "", 0, "ja"),
            new MangaSourceChapter("b-source", "42.1", "", 0, "en")
        };

        var canonical = MangaDexCanonicalChapterSelector.SelectOnePerLogicalChapter(chapters);

        Assert.Collection(
            canonical,
            chapter => Assert.Equal("a-source", chapter.Id),
            chapter => Assert.Equal("b-source", chapter.Id));
    }
}
