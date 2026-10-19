using System.Text.Json;
using System.Globalization;
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
public sealed class MangaDexTranslationCoverageService(
    MangaHubDbContext db,
    AdminIssueNotificationService issueNotifications,
    ILogger<MangaDexTranslationCoverageService> logger)
{
    private const decimal GapThreshold = 2m;

    public Task<TranslationCoverageCheckResult> RunAsync(CancellationToken cancellationToken) => RunAsync(null, cancellationToken);

    public async Task<TranslationCoverageCheckResult> RunAsync(Guid? mangaEntryId, CancellationToken cancellationToken)
    {
        var shelves = await (
            from shelf in db.UserMangaEntries.AsNoTracking()
            join user in db.Users.AsNoTracking() on shelf.UserId equals user.Id
            join manga in db.MangaEntries.AsNoTracking() on shelf.MangaEntryId equals manga.Id
            where manga.MangaDexId != ""
                && (mangaEntryId == null || manga.Id == mangaEntryId)
                && (shelf.ReadingStatus == "reading" || shelf.ReadingStatus == "paused")
            select new ShelfLanguageContext(manga.Id, manga.Title, manga.FallbackReaderUrl, user.PreferredLanguage, shelf.ExternalReaderLatestChapter))
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
            .Where(issue => (issue.Kind == AdminIssueTypes.MangaDexLanguageCoverage || issue.Kind == AdminIssueTypes.MangaDexTranslationAbandoned)
                && issue.SubjectType == AdminIssueTypes.CatalogManga
                && issue.Status == "open")
            .ToListAsync(cancellationToken);

        var evaluated = 0;
        var openedOrUpdated = 0;
        var resolved = 0;
        var now = DateTimeOffset.UtcNow;
        var coverageIssues = NormalizeLegacyIssues(openIssues, now);

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
                .Select(item => Evaluate(item, latest, allLanguageLatest))
                .Where(item => item.Gap > GapThreshold)
                .GroupBy(item => string.Join(",", item.PreferredLanguages), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.Gap).First())
                .OrderByDescending(item => item.Gap)
                .ToList();

            if (affected.Count == 0)
            {
                if (coverageIssues.TryGetValue(mangaShelves.Key, out var recovered))
                {
                    recovered.Status = "resolved";
                    recovered.ResolvedAt = now;
                    recovered.UpdatedAt = now;
                    recovered.ResolutionNote = "Recorded readable coverage, including any external reader, is within two chapters of the latest MangaDex language.";
                    resolved++;
                }
                continue;
            }

            var representative = mangaShelves.First();
            var maximumGap = affected.Max(item => item.Gap);
            var priority = GetPriority(maximumGap);
            var metadata = JsonSerializer.Serialize(new
            {
                latestAnyLanguageChapter = allLanguageLatest,
                threshold = GapThreshold,
                maximumChapterGap = maximumGap,
                priority,
                affectedLanguageSets = affected.Select(item => new
                {
                    preferredLanguages = item.PreferredLanguages,
                    latestPreferredLanguageChapter = item.PreferredLatest,
                    latestExternalReaderChapter = item.ExternalLatest,
                    latestReadableChapter = item.ReadableLatest,
                    chaptersBehind = item.Gap
                }),
                availableLanguages = latest
                    .OrderByDescending(item => item.LatestChapter)
                    .Select(item => new { language = item.Language, latestChapter = item.LatestChapter }),
                fallbackReaderUrl = representative.FallbackReaderUrl,
                checkedAt = now
            });

            AdminIssue issue;
            if (coverageIssues.TryGetValue(mangaShelves.Key, out var existing))
            {
                existing.MetadataJson = metadata;
                existing.Priority = priority;
                existing.UpdatedAt = now;
                issue = existing;
            }
            else
            {
                issue = new AdminIssue
                {
                    Kind = AdminIssueTypes.MangaDexLanguageCoverage,
                    SubjectType = AdminIssueTypes.CatalogManga,
                    SubjectId = mangaShelves.Key,
                    Priority = priority,
                    TitleSnapshot = $"MangaDex language coverage: {representative.Title}",
                    MetadataJson = metadata,
                    ResolutionNote = "Add an external fallback reader for the affected languages, or wait for MangaDex translations to catch up."
                };
                db.AdminIssues.Add(issue);
            }
            await issueNotifications.NotifyCoverageGapAsync(issue, new MangaEntry { Id = mangaShelves.Key, Title = representative.Title }, maximumGap, priority, cancellationToken);
            openedOrUpdated++;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("MangaDex translation coverage checked {MangaCount} manga, opened or refreshed {IssueCount} issues, and resolved {ResolvedCount} recovered issues.", evaluated, openedOrUpdated, resolved);
        return new TranslationCoverageCheckResult(evaluated, openedOrUpdated, resolved);
    }

    public async Task<bool> HasSufficientExternalCoverageAsync(Guid userId, Guid mangaEntryId, CancellationToken cancellationToken)
    {
        var shelf = await db.UserMangaEntries.AsNoTracking()
            .Where(entry => entry.UserId == userId && entry.MangaEntryId == mangaEntryId && entry.MangaEntry != null)
            .Select(entry => new { entry.ExternalReaderLatestChapter, entry.MangaEntry!.FallbackReaderUrl })
            .FirstOrDefaultAsync(cancellationToken);
        if (shelf is null || string.IsNullOrWhiteSpace(shelf.FallbackReaderUrl)
            || !decimal.TryParse(shelf.ExternalReaderLatestChapter?.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var externalLatest)
            || externalLatest < 0) return false;

        var latest = await db.MangaDexLanguageLatestChapters.AsNoTracking()
            .Where(chapter => chapter.MangaEntryId == mangaEntryId)
            .Select(chapter => (decimal?)chapter.LatestChapter)
            .MaxAsync(cancellationToken);
        return latest is not null && latest.Value - externalLatest <= GapThreshold;
    }

    private static TranslationCoverageEvaluation Evaluate(ShelfLanguageContext shelf, IReadOnlyList<MangaDexLanguageLatestChapter> latest, decimal allLanguageLatest)
    {
        var preferredLanguages = LanguagePreferences.Parse(shelf.PreferredLanguage);
        var preferredLatest = latest
            .Where(item => LanguagePreferences.Contains(preferredLanguages, item.Language))
            .Select(item => (decimal?)item.LatestChapter)
            .Max();
        decimal? externalLatest = !string.IsNullOrWhiteSpace(shelf.FallbackReaderUrl)
            && decimal.TryParse(shelf.ExternalReaderLatestChapter?.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 0 ? parsed : null;
        var readableLatest = externalLatest.HasValue && (!preferredLatest.HasValue || externalLatest.Value > preferredLatest.Value) ? externalLatest : preferredLatest;
        return new TranslationCoverageEvaluation(preferredLanguages, preferredLatest, externalLatest, readableLatest, allLanguageLatest - (readableLatest ?? 0m));
    }

    private static string GetPriority(decimal maximumGap) => maximumGap switch
    {
        >= 25m => "critical",
        >= 10m => "high",
        >= 5m => "normal",
        _ => "low"
    };

    private static Dictionary<Guid, AdminIssue> NormalizeLegacyIssues(IEnumerable<AdminIssue> issues, DateTimeOffset now)
    {
        var canonicalIssues = new Dictionary<Guid, AdminIssue>();
        foreach (var group in issues.GroupBy(issue => issue.SubjectId))
        {
            var canonical = group.FirstOrDefault(issue => issue.Kind == AdminIssueTypes.MangaDexLanguageCoverage);
            foreach (var legacy in group.Where(issue => issue.Kind == AdminIssueTypes.MangaDexTranslationAbandoned))
            {
                if (canonical is null)
                {
                    legacy.Kind = AdminIssueTypes.MangaDexLanguageCoverage;
                    canonical = legacy;
                }
                else
                {
                    legacy.Status = "resolved";
                    legacy.ResolvedAt = now;
                    legacy.UpdatedAt = now;
                    legacy.ResolutionNote = "Merged into the MangaDex language coverage issue for this manga.";
                }
            }

            if (canonical is not null)
            {
                canonicalIssues[group.Key] = canonical;
            }
        }

        return canonicalIssues;
    }

    private sealed record ShelfLanguageContext(Guid MangaEntryId, string Title, string FallbackReaderUrl, string PreferredLanguage, string ExternalReaderLatestChapter);
    private sealed record TranslationCoverageEvaluation(IReadOnlyList<string> PreferredLanguages, decimal? PreferredLatest, decimal? ExternalLatest, decimal? ReadableLatest, decimal Gap);
}

public sealed record TranslationCoverageCheckResult(int EvaluatedManga, int OpenedOrUpdatedIssues, int ResolvedIssues);
