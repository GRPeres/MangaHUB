using System.Text;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;

namespace MangaHub.Api.Tests;

public sealed class ShelfExportServiceTests
{
    [Fact]
    public void CreateCsv_ExportsImporterCompatibleFieldsAndEscapesExcelValues()
    {
        var entry = Entry with
        {
            Title = "A, Manga",
            Summary = "A \"quoted\" summary",
            MangaDexId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            Score = 4,
            IsRead = true
        };

        var csv = Encoding.UTF8.GetString(new ShelfExportService().CreateCsv([entry])).TrimStart('\uFEFF');

        Assert.StartsWith("Name,Link,Status,Chapter,Rating,Type,Summary,Notes", csv);
        Assert.Contains("\"A, Manga\"", csv);
        Assert.Contains("https://mangadex.org/title/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", csv);
        Assert.Contains("\"A \"\"quoted\"\" summary\"", csv);
        Assert.Contains(",true,", csv);
    }

    [Fact]
    public void CreateCsv_PrefixesFormulaLikeCellsForExcelSafety()
    {
        var csv = Encoding.UTF8.GetString(new ShelfExportService().CreateCsv([Entry with { Title = "=SUM(1,1)" }])).TrimStart('\uFEFF');

        Assert.Contains("\"\t=SUM(1,1)\"", csv);
    }

    private static readonly MangaEntryResponse Entry = new(
        Guid.NewGuid(), "Example Manga", "Author", "Fantasy", "Catalog summary", "", "", 2020,
        "myanimelist", "123", "Manga", "ongoing", 12, 2, "reading", "", null, null, "", null,
        "", null, null, null, "4", null, "", "", "", "", "mangahub", 8, false);
}
