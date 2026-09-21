using System.Globalization;

namespace MangaHub.Core.Services;

public static class MangaDexCacheRetentionPolicy
{
    public static bool ShouldRetain(string sourceId, string chapterNumber, decimal? earliestActiveChapter)
    {
        if (sourceId.StartsWith("manual-", StringComparison.Ordinal))
        {
            return true;
        }

        if (earliestActiveChapter is null)
        {
            return false;
        }

        var chapter = ParseChapterNumber(chapterNumber);
        return chapter is null || chapter.Value >= earliestActiveChapter.Value;
    }

    public static decimal? ParseChapterNumber(string? value)
    {
        var normalized = new string((value ?? "")
            .Where(character => char.IsDigit(character) || character is '.' or ',')
            .ToArray())
            .Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    public static decimal? FindEarliestRecordedChapter(IEnumerable<string?> chapters)
    {
        decimal? earliest = null;
        foreach (var chapter in chapters)
        {
            var parsed = ParseChapterNumber(chapter);
            if (parsed is not null && (earliest is null || parsed.Value < earliest.Value))
            {
                earliest = parsed.Value;
            }
        }

        return earliest;
    }
}
