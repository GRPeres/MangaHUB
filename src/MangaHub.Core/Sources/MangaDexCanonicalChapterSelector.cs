using System.Globalization;

namespace MangaHub.Core.Sources;

public static class MangaDexCanonicalChapterSelector
{
    public static IReadOnlyList<MangaSourceChapter> SelectOnePerLogicalChapter(IEnumerable<MangaSourceChapter> chapters) =>
        chapters
            .Where(chapter => !string.IsNullOrWhiteSpace(chapter.Id) && !string.IsNullOrWhiteSpace(chapter.Number))
            .GroupBy(GetLogicalKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(chapter => chapter.Id, StringComparer.Ordinal)
                .First())
            .OrderBy(chapter => ParseNumber(chapter.Number) ?? decimal.MaxValue)
            .ThenBy(chapter => chapter.Number, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static string GetLogicalKey(MangaSourceChapter chapter)
    {
        var number = ParseNumber(chapter.Number);
        return number is null
            ? chapter.Number.Trim().ToLowerInvariant()
            : number.Value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public static decimal? ParseNumber(string value)
    {
        var normalized = new string((value ?? "")
            .Where(character => char.IsDigit(character) || character is '.' or ',')
            .ToArray())
            .Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }
}
