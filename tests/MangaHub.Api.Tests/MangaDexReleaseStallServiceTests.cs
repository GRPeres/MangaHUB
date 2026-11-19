using MangaHub.Api.Services;
using MangaHub.Core.Models;
using MangaHub.Core.Services;

namespace MangaHub.Api.Tests;

public sealed class MangaDexReleaseStallServiceTests
{
    [Fact]
    public async Task RunAsync_CreatesIssueAfterThirtyUninterruptedOngoingDays()
    {
        await using var db = TestDb.Create();
        var observedAt = DateTimeOffset.UtcNow.AddDays(-31);
        var manga = new MangaEntry
        {
            Title = "Quiet ongoing series",
            MangaDexId = "manga-id",
            PublishingStatus = "ongoing",
            MangaDexLatestChapter = 42,
            MangaDexLatestChapterObservedAt = observedAt,
            MangaDexNonHiatusSince = observedAt
        };
        db.MangaEntries.Add(manga);
        await db.SaveChangesAsync();

        var result = await CreateService(db).RunAsync(CancellationToken.None);

        var issue = Assert.Single(db.AdminIssues);
        Assert.Equal(AdminIssueTypes.MangaDexReleaseStalled, issue.Kind);
        Assert.Equal(manga.Id, issue.SubjectId);
        Assert.Equal(1, result.StalledManga);
        Assert.Contains("42", issue.MetadataJson);
    }

    [Theory]
    [InlineData("hiatus")]
    [InlineData("completed")]
    public async Task RunAsync_DoesNotFlagHiatusOrNonOngoingSeries(string status)
    {
        await using var db = TestDb.Create();
        var observedAt = DateTimeOffset.UtcNow.AddDays(-60);
        db.MangaEntries.Add(new MangaEntry
        {
            Title = "Excluded",
            MangaDexId = "manga-id",
            PublishingStatus = status,
            MangaDexLatestChapter = 42,
            MangaDexLatestChapterObservedAt = observedAt,
            MangaDexNonHiatusSince = observedAt
        });
        await db.SaveChangesAsync();

        var result = await CreateService(db).RunAsync(CancellationToken.None);

        Assert.Equal(0, result.StalledManga);
        Assert.Empty(db.AdminIssues);
    }

    [Fact]
    public async Task RunAsync_ResolvesWhenAReleaseArrives()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry
        {
            Title = "Recovered",
            MangaDexId = "manga-id",
            PublishingStatus = "ongoing",
            MangaDexLatestChapter = 42,
            MangaDexLatestChapterObservedAt = DateTimeOffset.UtcNow.AddDays(-1),
            MangaDexNonHiatusSince = DateTimeOffset.UtcNow.AddDays(-60)
        };
        var issue = new AdminIssue
        {
            Kind = AdminIssueTypes.MangaDexReleaseStalled,
            SubjectType = AdminIssueTypes.CatalogManga,
            SubjectId = manga.Id,
            TitleSnapshot = manga.Title
        };
        db.AddRange(manga, issue);
        await db.SaveChangesAsync();

        var result = await CreateService(db).RunAsync(CancellationToken.None);

        Assert.Equal(1, result.ResolvedIssues);
        Assert.Equal("resolved", issue.Status);
        Assert.Equal("auto_resolved", issue.ReviewState);
    }

    [Fact]
    public async Task RunAsync_RefreshesAnExistingIssueTitleWithTheCurrentInactiveDays()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry
        {
            Title = "Still quiet",
            MangaDexId = "manga-id",
            PublishingStatus = "ongoing",
            MangaDexLatestChapter = 42,
            MangaDexLatestChapterObservedAt = DateTimeOffset.UtcNow.AddDays(-45),
            MangaDexNonHiatusSince = DateTimeOffset.UtcNow.AddDays(-60)
        };
        var issue = new AdminIssue
        {
            Kind = AdminIssueTypes.MangaDexReleaseStalled,
            SubjectType = AdminIssueTypes.CatalogManga,
            SubjectId = manga.Id,
            TitleSnapshot = "No MangaDex chapter for 30 days: Still quiet"
        };
        db.AddRange(manga, issue);
        await db.SaveChangesAsync();

        await CreateService(db).RunAsync(CancellationToken.None);

        Assert.Matches(@"^No MangaDex chapter for 4[45] days: Still quiet$", issue.TitleSnapshot);
    }

    [Fact]
    public async Task RunAsync_UsesMangaDexPublishDateInsteadOfTheFirstObservedDate()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry
        {
            Title = "Recently published",
            MangaDexId = "manga-id",
            PublishingStatus = "ongoing",
            MangaDexLatestChapter = 42,
            MangaDexLatestChapterObservedAt = DateTimeOffset.UtcNow.AddDays(-90),
            MangaDexLatestChapterPublishedAt = DateTimeOffset.UtcNow.AddDays(-2),
            MangaDexNonHiatusSince = DateTimeOffset.UtcNow.AddDays(-90)
        };
        db.MangaEntries.Add(manga);
        await db.SaveChangesAsync();

        var result = await CreateService(db).RunAsync(CancellationToken.None);

        Assert.Equal(0, result.StalledManga);
        Assert.Empty(db.AdminIssues);
    }

    [Fact]
    public async Task RunAsync_CountsInactivityFromResumptionAfterHiatus()
    {
        await using var db = TestDb.Create();
        db.MangaEntries.Add(new MangaEntry
        {
            Title = "Resumed series",
            MangaDexId = "manga-id",
            PublishingStatus = "ongoing",
            MangaDexLatestChapter = 5,
            MangaDexLatestChapterPublishedAt = DateTimeOffset.UtcNow.AddDays(-200),
            MangaDexNonHiatusSince = DateTimeOffset.UtcNow.AddDays(-40)
        });
        await db.SaveChangesAsync();

        await CreateService(db).RunAsync(CancellationToken.None);

        var issue = Assert.Single(db.AdminIssues);
        Assert.Matches(@"^No MangaDex chapter for 4[01] days:", issue.TitleSnapshot);
        Assert.Equal("normal", issue.Priority);
    }

    [Theory]
    [InlineData("https://example.org/read", 50, 42, "low")]
    [InlineData("", 50, 42, "high")]
    [InlineData("https://example.org/read", 41, 42, "high")]
    [InlineData("https://example.org/read", null, 42, "high")]
    [InlineData("https://example.org/read", 50, null, "high")]
    public async Task RunAsync_LowersPriorityOnlyWhenExternalEvidenceCoversMangaDex(
        string url, int? external, int? latest, string expectedPriority)
    {
        await using var db = TestDb.Create();
        db.MangaEntries.Add(new MangaEntry
        {
            Title = "External coverage",
            MangaDexId = "manga-id",
            PublishingStatus = "ongoing",
            MangaDexLatestChapter = latest,
            MangaDexLatestChapterPublishedAt = DateTimeOffset.UtcNow.AddDays(-100),
            MangaDexNonHiatusSince = DateTimeOffset.UtcNow.AddDays(-100),
            FallbackReaderUrl = url,
            ExternalReaderLatestChapter = external
        });
        await db.SaveChangesAsync();

        await CreateService(db).RunAsync(CancellationToken.None);

        var issue = Assert.Single(db.AdminIssues);
        Assert.Equal(expectedPriority, issue.Priority);
        Assert.Equal(expectedPriority == "low" ? "waiting" : "monitoring", issue.ReviewState);
        Assert.NotNull(issue.LastCheckedAt);
    }

    [Fact]
    public async Task RunAsync_PromotesFreshIndependentGapButNotWhenExternalCoversIt()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry
        {
            Title = "Gap", MangaDexId = "md", PublishingStatus = "ongoing",
            MangaDexLatestChapter = 10, MangaDexLatestChapterPublishedAt = DateTimeOffset.UtcNow.AddDays(-40),
            MangaDexNonHiatusSince = DateTimeOffset.UtcNow.AddDays(-40),
            MangaUpdatesId = "mu", MangaUpdatesLatestChapter = 20, MangaUpdatesLastSyncedAt = DateTimeOffset.UtcNow
        };
        db.Add(manga);
        await db.SaveChangesAsync();
        await CreateService(db).RunAsync(default);
        var issue = Assert.Single(db.AdminIssues);
        Assert.Equal("monitoring", issue.ReviewState);
        await CreateService(db).RunAsync(default);
        Assert.Equal("monitoring", issue.ReviewState);
        manga.MangaUpdatesLastSyncedAt = manga.MangaUpdatesLastSyncedAt!.Value.AddMinutes(1);
        await db.SaveChangesAsync();
        await CreateService(db).RunAsync(default);
        Assert.Equal("action_required", issue.ReviewState);
        Assert.Equal("high", issue.Priority);
        var inbox = await new MangaHub.Api.Repositories.AdminIssueRepository(db).ListAsync("action", 0, 40, default);
        Assert.Equal("MangaDex has chapter 10; MangaUpdates lists chapter 20 as a publication reference.",
            Assert.Single(inbox).Diagnosis);

        manga.FallbackReaderUrl = "https://example.org/read";
        manga.ExternalReaderLatestChapter = 20;
        await db.SaveChangesAsync();
        await CreateService(db).RunAsync(default);
        Assert.Equal("waiting", issue.ReviewState);
    }

    [Fact]
    public async Task RunAsync_RespectsLearnedReleaseWindow()
    {
        await using var db = TestDb.Create();
        db.Add(new MangaEntry
        {
            Title = "Slow cadence", MangaDexId = "md", PublishingStatus = "ongoing",
            MangaDexLatestChapter = 10, MangaDexLatestChapterPublishedAt = DateTimeOffset.UtcNow.AddDays(-45),
            MangaDexNonHiatusSince = DateTimeOffset.UtcNow.AddDays(-120),
            MangaDexReleaseIntervalDays = 60, MangaDexReleaseIntervalSamples = 3
        });
        await db.SaveChangesAsync();
        await CreateService(db).RunAsync(default);
        Assert.Empty(db.AdminIssues);
    }

    [Fact]
    public async Task Inbox_SeparatesActionableSnoozedAndHistoricalIssues()
    {
        await using var db = TestDb.Create();
        var action = new AdminIssue();
        var snoozed = new AdminIssue { SnoozedUntil = DateTimeOffset.UtcNow.AddDays(7) };
        var history = new AdminIssue { Status = "dismissed" };
        db.AddRange(action, snoozed, history);
        await db.SaveChangesAsync();
        var repository = new MangaHub.Api.Repositories.AdminIssueRepository(db);
        Assert.Equal(action.Id, Assert.Single(await repository.ListAsync("action", 0, 40, default)).Id);
        Assert.Equal(snoozed.Id, Assert.Single(await repository.ListAsync("review", 0, 40, default)).Id);
        Assert.Equal(history.Id, Assert.Single(await repository.ListAsync("history", 0, 40, default)).Id);
        Assert.Equal(1, await repository.CountOpenAsync(default));
        var now = DateTimeOffset.UtcNow;
        action.CreatedAt = now.AddDays(-10);
        db.AddRange(
            new AdminIssue { Status = "resolved", ReviewState = "auto_resolved", ResolvedAt = now.AddDays(-2) },
            new AdminIssue { Status = "resolved", ReviewState = "auto_resolved", ResolvedAt = now.AddDays(-8) },
            new AdminIssue { Status = "resolved", ResolvedAt = now.AddDays(-1) });
        await db.SaveChangesAsync();

        var health = await repository.GetHealthAsync(default);
        Assert.Equal(1, health.Actionable);
        Assert.Equal(1, health.InReview);
        Assert.Equal(1, health.AutoResolvedLastSevenDays);
        Assert.Equal(action.CreatedAt, health.OldestOpenAt);
    }

    private static MangaDexReleaseStallService CreateService(MangaHub.Infrastructure.Data.MangaHubDbContext db) =>
        new(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<MangaDexReleaseStallService>.Instance);
}
