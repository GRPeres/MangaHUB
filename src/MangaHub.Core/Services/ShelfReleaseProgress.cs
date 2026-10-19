using System.Globalization;

namespace MangaHub.Core.Services;

public static class ShelfReleaseProgress
{
    public static decimal? LatestChapter(bool hasMangaDex, decimal? mangaDexLatest, string? externalLatest)
    {
        var external = ParseChapter(externalLatest);
        var mangaDex = hasMangaDex ? mangaDexLatest : null;
        return mangaDex is null ? external : external is null ? mangaDex : Math.Max(mangaDex.Value, external.Value);
    }

    public static int NewChapterCount(string? current, bool isRead, bool hasMangaDex, decimal? mangaDexLatest, string? externalLatest)
    {
        var currentChapter = ParseChapter(current);
        if (currentChapter is null) return 0;
        var latest = LatestChapter(hasMangaDex, mangaDexLatest, externalLatest);
        if (latest > currentChapter) return (int)Math.Ceiling(latest.Value - currentChapter.Value);
        return hasMangaDex && mangaDexLatest == currentChapter && !isRead ? 1 : 0;
    }

    private static decimal? ParseChapter(string? value)
    {
        var text = new string((value ?? "").SkipWhile(character => !char.IsDigit(character))
            .TakeWhile(character => char.IsDigit(character) || character is '.' or ',').ToArray()).Replace(',', '.');
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var chapter) ? chapter : null;
    }
}
