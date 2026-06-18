using MangaHub.Core.Sources;

namespace MangaHub.Core.Tests;

public sealed class MangaSourceContractsTests
{
    [Fact]
    public void SearchResult_CarriesSourceName()
    {
        var result = new MangaSearchResult("id", "Title", "", "", "unknown", "local");

        Assert.Equal("local", result.Source);
    }
}

