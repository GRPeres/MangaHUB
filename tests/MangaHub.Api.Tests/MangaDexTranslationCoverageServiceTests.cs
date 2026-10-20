using MangaHub.Api.Services;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Tests;

public sealed class MangaDexTranslationCoverageServiceTests
{
    [Fact]
    public async Task ExternalCheckIn_ReevaluatesOnlyThatMangaAndMakesTheSharedFallbackCoverAllUsers()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry { Title = "Linked", MangaDexId = "id", FallbackReaderUrl = "https://example.com" };
        var otherManga = new MangaEntry { Title = "Other", MangaDexId = "other" };
        var one = new MangaUser { Username = "one", PasswordHash = "hash", PreferredLanguage = "en" };
        var two = new MangaUser { Username = "two", PasswordHash = "hash", PreferredLanguage = "en" };
        db.AddRange(manga, otherManga, one, two,
            new UserMangaEntry { UserId = one.Id, MangaEntryId = manga.Id, ReadingStatus = "reading", CurrentChapter = "240" },
            new UserMangaEntry { UserId = two.Id, MangaEntryId = manga.Id, ReadingStatus = "reading", CurrentChapter = "240" },
            new UserMangaEntry { UserId = one.Id, MangaEntryId = otherManga.Id, ReadingStatus = "reading" },
            new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "en", LatestChapter = 240 },
            new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "es-la", LatestChapter = 258 },
            new MangaDexLanguageLatestChapter { MangaEntryId = otherManga.Id, Language = "fr", LatestChapter = 20 });
        await db.SaveChangesAsync();
        var coverage = CreateService(db);
        await coverage.RunAsync(manga.Id, CancellationToken.None);
        var issue = Assert.Single(db.AdminIssues);
        Assert.Equal("open", issue.Status);
        var shelf = new ShelfService(new ShelfRepository(db), new CatalogRepository(db), new UserRepository(db), coverage: coverage);

        Assert.True(await shelf.UpdateExternalReaderProgressAsync(one.Id, manga.Id, new ExternalReaderProgressRequest("240", "258"), CancellationToken.None));
        Assert.True(await coverage.HasSufficientExternalCoverageAsync(one.Id, manga.Id, CancellationToken.None));
        Assert.True(await coverage.HasSufficientExternalCoverageAsync(two.Id, manga.Id, CancellationToken.None));
        Assert.Equal("resolved", issue.Status);
        Assert.Single(db.AdminIssues);
        Assert.True(await shelf.UpdateExternalReaderProgressAsync(two.Id, manga.Id, new ExternalReaderProgressRequest("240", "258"), CancellationToken.None));
        Assert.Equal("resolved", issue.Status);
    }

    [Fact]
    public async Task ExternalCoverage_DoesNotAssumeMissingMangaDexDataIsCaughtUp()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry { Title = "Unknown", MangaDexId = "id", FallbackReaderUrl = "https://example.com" };
        var user = new MangaUser { Username = "reader", PasswordHash = "hash" };
        db.AddRange(manga, user, new UserMangaEntry { UserId = user.Id, MangaEntryId = manga.Id, ReadingStatus = "reading", ExternalReaderLatestChapter = "258" });
        await db.SaveChangesAsync();
        Assert.False(await CreateService(db).HasSufficientExternalCoverageAsync(user.Id, manga.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData("20", true, false)]
    [InlineData("17", true, true)]
    [InlineData("bad", true, true)]
    [InlineData("20", false, true)]
    public async Task RunAsync_UsesRecordedExternalCoverageOnlyWhenLinked(string externalLatest, bool linked, bool expectIssue)
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry { Title = "External", MangaDexId = "id", FallbackReaderUrl = linked ? "https://example.com" : "" };
        var user = new MangaUser { Username = "reader", PasswordHash = "hash", PreferredLanguage = "en" };
        db.AddRange(manga, user, new UserMangaEntry { UserId = user.Id, MangaEntryId = manga.Id, ReadingStatus = "reading", ExternalReaderLatestChapter = externalLatest });
        db.MangaDexLanguageLatestChapters.AddRange(new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "en", LatestChapter = 10 }, new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "fr", LatestChapter = 20 });
        await db.SaveChangesAsync();
        await CreateService(db).RunAsync(CancellationToken.None);
        Assert.Equal(expectIssue, db.AdminIssues.Any());
        if (externalLatest == "17" && linked) Assert.Contains("\"maximumChapterGap\":3", Assert.Single(db.AdminIssues).MetadataJson);
    }

    [Fact]
    public async Task RunAsync_ExternalCoverageIsSharedWhenTheCatalogFallbackCoversTheManga()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry { Title = "External", MangaDexId = "id", FallbackReaderUrl = "https://example.com" };
        var one = new MangaUser { Username = "one", PasswordHash = "hash", PreferredLanguage = "en" };
        var two = new MangaUser { Username = "two", PasswordHash = "hash", PreferredLanguage = "en" };
        var first = new UserMangaEntry { UserId = one.Id, MangaEntryId = manga.Id, ReadingStatus = "reading", ExternalReaderLatestChapter = "20" };
        var second = new UserMangaEntry { UserId = two.Id, MangaEntryId = manga.Id, ReadingStatus = "reading", ExternalReaderLatestChapter = "" };
        db.AddRange(manga, one, two, first, second, new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "en", LatestChapter = 10 }, new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "fr", LatestChapter = 20 });
        await db.SaveChangesAsync();
        await CreateService(db).RunAsync(CancellationToken.None);
        Assert.Empty(db.AdminIssues);
    }
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
        Assert.Equal(AdminIssueTypes.MangaDexLanguageCoverage, issue.Kind);
        Assert.Equal(1, result.OpenedOrUpdatedIssues);
        Assert.Contains("\"en\"", issue.MetadataJson);
        Assert.Contains("\"pt-br\"", issue.MetadataJson);
        Assert.Contains("20", issue.MetadataJson);
    }

    [Fact]
    public async Task RunAsync_MigratesLegacyIssueAndResolvesItWhenAnyPreferredLanguageCatchesUp()
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
        Assert.Equal(AdminIssueTypes.MangaDexLanguageCoverage, issue.Kind);
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

    [Fact]
    public async Task RunAsync_EscalatesSeriousGapsAndAlertsEachAdministratorOnlyOnce()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry { Title = "Seriously behind", MangaDexId = "manga-id" };
        var reader = new MangaUser { Username = "reader", PasswordHash = "hash", PreferredLanguage = "en" };
        var admin = new MangaUser { Username = "admin", PasswordHash = "hash", Role = "admin", PreferredLanguage = "en" };
        db.AddRange(manga, reader, admin);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = reader.Id, MangaEntryId = manga.Id, ReadingStatus = "reading" });
        db.MangaDexLanguageLatestChapters.AddRange(
            new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "en", LatestChapter = 10 },
            new MangaDexLanguageLatestChapter { MangaEntryId = manga.Id, Language = "fr", LatestChapter = 22 });
        await db.SaveChangesAsync();

        await CreateService(db).RunAsync(CancellationToken.None);
        await CreateService(db).RunAsync(CancellationToken.None);

        var issue = Assert.Single(db.AdminIssues);
        Assert.Equal("high", issue.Priority);
        var notification = Assert.Single(db.Notifications);
        Assert.Equal(admin.Id, notification.UserId);
        Assert.Contains("12", notification.Body);
        Assert.Contains("high", notification.Type, StringComparison.OrdinalIgnoreCase);
        Assert.True(notification.Type.Length <= 40);
    }

    private static MangaDexTranslationCoverageService CreateService(MangaHub.Infrastructure.Data.MangaHubDbContext db) =>
        new(
            db,
            new AdminIssueNotificationService(
                db,
                Options.Create(new MangaHubOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AdminIssueNotificationService>.Instance),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MangaDexTranslationCoverageService>.Instance);
}
