using MangaHub.Api.Services;
using MangaHub.Core.Models;
using MangaHub.Core.Services;

namespace MangaHub.Api.Tests;

public sealed class MangaDexTranslationCoverageServiceTests
{
    [Fact]
    public async Task RunAsync_UsesEveryPreferredLanguageAndCreatesOneRepairableIssue()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry { Title = "Falling behind", MangaDexId = "manga-id" };
        var englishOnly = new MangaUser { Username = "english", PasswordHash = "hash", PreferredLanguage = "en" };
        var EnglishThenPortuguese = new MangaUser { Username = "multi", PasswordHash = "hash", PreferredLanguage = "en,pt-br" };
        db.AddRange(manga, englishOnly, EnglishThenPortuguese);
        db.UserMangaEntries.AddRange(
            new UserMangaEntry { UserId = englishOnly.Id, MangaEntryId = manga.Id, ReadingStatus = "reading" },
            new UserMangaEntry { UserId = EnglishThenPortuguese.Id, MangaEntryId = manga.Id, ReadingStatus = "paused" });
        db.MangaDexLanguageLatestChapters.AddRange(
            new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "en", LatestChapter = 10 },
            new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "pt-br", LatestChapter = 17 },
            new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "fr", LatestChapter = 20 });
        await db.SaveChangesAsync();

        var result = await CreateService(db).RunAsync(CancellationToken.None);

        var issue = Assert.Single(db.AdminIssues);
        Assert.Equal(AdminIssueTypes.MangaDexTranslationAbandoned, issue.Kind);
        Assert.Equal(1, result.OpenedOrUpdatedIssues);
        Assert.Contains("\"en\"", issue.MetadataJson);
        Assert.Contains("\"pt-br\"", issue.MetadataJson);
        Assert.Contains("20", issue.MetadataJson);
    }

    [Fact]
    public async Task RunAsync_ResolvesIssueWhenAnyPreferredLanguageCatchesUp()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry { Title = "Recovered", MangaDexId = "manga-id" };
        var user = new MangaUser { Username = "reader", PasswordHash = "hash", PreferredLanguage = "en,pt-br" };
        var issue = new AdminIssue
        {
            Kind = AdminIssueTypes.MangaDexTranslationAbandoned,
            SubjectType = AdminIssueTypes.CatalogManga,
            SubjectId = manga.Id,
            TitleSnapshot = manga.Title
        };
        db.AddRange(manga, user, issue);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = user.Id, MangaEntryId = manga.Id, ReadingStatus = "reading" });
        db.MangaDexLanguageLatestChapters.AddRange(
            new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "en", LatestChapter = 20 },
            new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "fr", LatestChapter = 21 });
        await db.SaveChangesAsync();

        var result = await CreateService(db).RunAsync(CancellationToken.None);

        Assert.Equal(1, result.ResolvedIssues);
        Assert.Equal("resolved", issue.Status);
    }

    [Fact]
    public async Task RunAsync_IgnoresPlannedAndDoneShelfEntries()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry { Title = "Not active", MangaDexId = "manga-id" };
        var user = new MangaUser { Username = "reader", PasswordHash = "hash", PreferredLanguage = "en" };
        db.AddRange(manga, user);
        db.UserMangaEntries.AddRange(
            new UserMangaEntry { UserId = user.Id, MangaEntryId = manga.Id, ReadingStatus = "planned" },
            new UserMangaEntry { UserId = user.Id, MangaEntryId = manga.Id, ReadingStatus = "done" });
        db.MangaDexLanguageLatestChapters.AddRange(
            new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "en", LatestChapter = 1 },
            new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "fr", LatestChapter = 10 });
        await db.SaveChangesAsync();

        var result = await CreateService(db).RunAsync(CancellationToken.None);

        Assert.Equal(0, result.EvaluatedManga);
        Assert.Empty(db.AdminIssues);
    }

    private static MangaDexTranslationCoverageService CreateService(MangaHub.Infrastructure.Data.MangaHubDbContext db) =>
        new(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<MangaDexTranslationCoverageService>.Instance);
}
