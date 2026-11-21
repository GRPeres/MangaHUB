using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Services;

public static class PlannedChapterPrefetch
{
    public static MangaSourceChapter? SelectOpeningChapter(
        IEnumerable<MangaSourceChapter> chapters, IReadOnlyList<string> languages) =>
        SelectOpeningChapter(chapters.Where(chapter => chapter.PageCount > 0), languages,
            chapter => chapter.Language, chapter => chapter.Number);

    public static async Task<Dictionary<string, string[]>> ReadPreferencesAsync(MangaHubDbContext db, CancellationToken token)
    {
        var planned = await (
            from shelf in db.UserMangaEntries
            join user in db.Users on shelf.UserId equals user.Id
            where shelf.ReadingStatus == "planned" && shelf.CurrentChapter == "" && shelf.MangaEntry!.MangaDexId != ""
            select new { shelf.MangaEntry!.MangaDexId, user.PreferredLanguage }).Distinct().ToListAsync(token);
        return planned.GroupBy(row => row.MangaDexId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(row => row.PreferredLanguage).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    public static HashSet<string> ProtectedChapterIds(MangaSeries series, IReadOnlyDictionary<string, string[]> preferences)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!preferences.TryGetValue(series.ExternalId, out var languageLists)) return result;
        foreach (var list in languageLists)
        {
            var chapter = SelectOpeningChapter(series.Chapters.Where(chapter => chapter.PageCount > 0),
                LanguagePreferences.Parse(list), chapter => chapter.Language, chapter => chapter.ChapterNumber);
            if (chapter is not null) result.Add(chapter.SourceId);
        }
        return result;
    }

    private static T? SelectOpeningChapter<T>(IEnumerable<T> chapters, IReadOnlyList<string> languages,
        Func<T, string> language, Func<T, string> number) where T : class =>
        chapters.Where(chapter => LanguagePreferences.Contains(languages, language(chapter)))
            .OrderBy(chapter => LanguagePreferences.IndexOf(languages, language(chapter)))
            .ThenBy(number, ChapterNumberComparer.Instance)
            .FirstOrDefault();
}
