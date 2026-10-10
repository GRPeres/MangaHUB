using System.Text.Json;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Services;

/// <summary>
/// Finds MangaDex series whose readable translations have fallen materially behind
/// another page-bearing language. The issue is catalog-wide but its evidence is
/// calculated from every affected shelf owner's ordered language list.
/// </summary>
public sealed class MangaDexTranslationCoverageService(MangaHubDbContext db, ILogger<MangaDexTranslationCoverageService> logger)
{
    private const decimal GapThreshold = 2m;

    public async Task<TranslationCoverageCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        var shelves = await (
            from shelf in db.UserMangaEntries.AsNoTracking()
            join user in db.Users.AsNoTracking() on shelf.UserId equals user.Id
            join manga in db.MangaEntries.AsNoTracking() on shelf.MangaEntryId equals manga.Id
            where manga.MangaDexId != ""
                && (shelf.ReadingStatus == "reading" || shelf.ReadingStatus == "paused")
            select new ShelfLanguageContext(manga.Id, manga.Title, manga.FallbackReaderUrl, user.PreferredLanguage))
            .ToListAsync(cancellationToken);

        if (shelves.Count == 0)
        {
            return new TranslationCoverageCheckResult(0, 0, 0);
        }

        var mangaIds = shelves.Select(item => item.MangaEntryId).Distinct().ToArray();
        var latestByManga = (await db.MangaDexLanguageLatestChapters.AsNoTracking()
                .Where(item => mangaIds.Contains(item.MangaEntryId))
                .ToListAsync(cancellationToken))
            .GroupBy(item => item.MangaEntryId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var openIssues = await db.AdminIssues
            .Where(issue => issue.Kind == AdminIssueTypes.MangaDexTranslationAbandoned
                && issue.SubjectType == AdminIssueTypes.CatalogManga
                && issue.Status == "open")
            .ToDictionaryAsync(issue => issue.SubjectId, cancellationToken);

        var evaluated = 0;
        var openedOrUpdated = 0;
        var resolved = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var mangaShelves in shelves.GroupBy(item => item.MangaEntryId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!latestByManga.TryGetValue(mangaShelves.Key, out var latest) || latest.Count == 0)
            {
                continue;
            }

            evaluated++;
            var allLanguageLatest = latest.Max(item => item.LatestChapter);
            var affected = mangaShelves
                .Select(item => Evaluate(item.PreferredLanguage, latest, allLanguageLatest))
                .Where(item => item.Gap > GapThreshold)
                .GroupBy(item => string.Join(",", item.PreferredLanguages), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.Gap).First())
                .OrderByDescending(item => item.Gap)
                .ToList();

            if (affected.Count == 0)
            {
                if (openIssues.TryGetValue(mangaShelves.Key, out var recovered))
                {
                    recovered.Status = "resolved";
                    recovered.ResolvedAt = now;
                    recovered.UpdatedAt = now;
                    recovered.ResolutionNote = "Preferred MangaDex translation coverage is within two chapters of the latest available language again.";
                    resolved++;
                }
                continue;
            }

            var representative = mangaShelves.First();
            var metadata = JsonSerializer.Serialize(new
            {
                latestAnyLanguageChapter = allLanguageLatest,
                threshold = GapThreshold,
                affectedLanguageSets = affected.Select(item => new
                {
                    preferredLanguages = item.PreferredLanguages,
                    latestPreferredLanguageChapter = item.PreferredLatest,
                    chaptersBehind = item.Gap
                }),
                availableLanguages = latest
                    .OrderByDescending(item => item.LatestChapter)
                    .Select(item => new { language = item.Language, latestChapter = item.LatestChapter }),
                fallbackReaderUrl = representative.FallbackReaderUrl,
                checkedAt = now
            });

            if (openIssues.TryGetValue(mangaShelves.Key, out var existing))
            {
                existing.MetadataJson = metadata;
                existing.Priority = affected.Max(item => item.Gap) >= 10 ? "high" : "normal";
                existing.UpdatedAt = now;
            }
            else
            {
                db.AdminIssues.Add(new AdminIssue
                {
                    Kind = AdminIssueTypes.MangaDexTranslationAbandoned,
                    SubjectType = AdminIssueTypes.CatalogManga,
                    SubjectId = mangaShelves.Key,
                    Priority = affected.Max(item => item.Gap) >= 10 ? "high" : "normal",
                    TitleSnapshot = $"Translation coverage behind: {representative.Title}",
                    MetadataJson = metadata,
                    ResolutionNote = "Add an external fallback reader for the affected languages, or wait for MangaDex translations to catch up."
                });
            }
            openedOrUpdated++;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("MangaDex translation coverage checked {MangaCount} manga, opened or refreshed {IssueCount} issues, and resolved {ResolvedCount} recovered issues.", evaluated, openedOrUpdated, resolved);
        return new TranslationCoverageCheckResult(evaluated, openedOrUpdated, resolved);
    }

    private static TranslationCoverageEvaluation Evaluate(string preferredLanguage, IReadOnlyList<MangaDexLanguageLatestChapter> latest, decimal allLanguageLatest)
    {
        var preferredLanguages = LanguagePreferences.Parse(preferredLanguage);
        var preferredLatest = latest
            .Where(item => LanguagePreferences.Contains(preferredLanguages, item.Language))
            .Select(item => (decimal?)item.LatestChapter)
            .Max();
        return new TranslationCoverageEvaluation(preferredLanguages, preferredLatest, allLanguageLatest - (preferredLatest ?? 0m));
    }

    private sealed record ShelfLanguageContext(Guid MangaEntryId, string Title, string FallbackReaderUrl, string PreferredLanguage);
    private sealed record TranslationCoverageEvaluation(IReadOnlyList<string> PreferredLanguages, decimal? PreferredLatest, decimal Gap);
}

public sealed record TranslationCoverageCheckResult(int EvaluatedManga, int OpenedOrUpdatedIssues, int ResolvedIssues);
