using System.Text.Json;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Services;

public static class IssueProposalEvidence
{
    private static readonly string[] IssueFields =
    [
        "latestChapter", "referenceChapter", "referenceGap", "externalReaderLatestChapter",
        "latestAnyLanguageChapter", "maximumChapterGap", "expectedMyAnimeListId",
        "linkedMyAnimeListId", "linkedMangaDexId", "linkedTitle"
    ];

    public static SortedDictionary<string, string> ReadIssueFacts(string metadata)
    {
        using var document = JsonDocument.Parse(metadata);
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return facts;
        foreach (var key in IssueFields)
            if (root.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                facts[key] = Limit(value.ToString(), 160);
        if (root.TryGetProperty("affectedLanguageSets", out var sets) && sets.ValueKind == JsonValueKind.Array)
        {
            var affected = sets.EnumerateArray().Where(set => set.ValueKind == JsonValueKind.Object
                    && set.TryGetProperty("preferredLanguages", out var list) && list.ValueKind == JsonValueKind.Array)
                .SelectMany(set => set.GetProperty("preferredLanguages").EnumerateArray())
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!)
                .Where(value => value.Length is >= 2 and <= 12 && value.All(ch => char.IsAsciiLetter(ch) || ch == '-'))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Take(30);
            facts["affectedLanguages"] = string.Join(", ", affected);
        }
        return facts;
    }

    public static async Task<object?> ReadCatalogAsync(MangaHubDbContext db, Guid subjectId, CancellationToken token)
    {
        var manga = await db.MangaEntries.AsNoTracking().Where(row => row.Id == subjectId)
            .Select(row => new
            {
                row.Title, row.Authors, row.MediaType, row.FirstPublishYear, row.PublishingStatus, row.PublishingHiatusOverride,
                row.MyAnimeListId, row.MangaDexId, row.MangaUpdatesId,
                row.MangaDexLatestChapter, row.MangaDexLatestChapterPublishedAt, row.MangaDexLastSyncedAt,
                row.MangaDexReleaseIntervalDays, row.MangaDexReleaseIntervalSamples,
                row.MangaUpdatesLatestChapter, row.MangaUpdatesStatus, row.MangaUpdatesCompleted, row.MangaUpdatesLastSyncedAt,
                HasExternalReader = row.FallbackReaderUrl != "", row.ExternalReaderLatestChapter, row.ExternalReaderLatestChapterObservedAt
            }).FirstOrDefaultAsync(token);
        if (manga is null) return null;
        var languages = await db.MangaDexLanguageLatestChapters.AsNoTracking().Where(row => row.MangaEntryId == subjectId)
            .OrderByDescending(row => row.LatestChapter).ThenBy(row => row.Language).Take(40)
            .Select(row => new { row.Language, row.LatestChapter, row.SyncedAt }).ToListAsync(token);
        return new
        {
            Title = Limit(manga.Title, 255), Authors = Limit(manga.Authors, 250), manga.MediaType, manga.FirstPublishYear,
            manga.PublishingStatus, manga.PublishingHiatusOverride,
            publicIds = new { manga.MyAnimeListId, manga.MangaDexId, manga.MangaUpdatesId },
            mangaDex = new { manga.MangaDexLatestChapter, manga.MangaDexLatestChapterPublishedAt, manga.MangaDexLastSyncedAt,
                manga.MangaDexReleaseIntervalDays, manga.MangaDexReleaseIntervalSamples, latestByLanguage = languages },
            publicationReference = new { manga.MangaUpdatesLatestChapter, manga.MangaUpdatesStatus,
                manga.MangaUpdatesCompleted, manga.MangaUpdatesLastSyncedAt },
            externalReader = new { manga.HasExternalReader, manga.ExternalReaderLatestChapter, manga.ExternalReaderLatestChapterObservedAt }
        };
    }

    private static string Limit(string value, int limit) => value[..Math.Min(value.Length, limit)];
}
