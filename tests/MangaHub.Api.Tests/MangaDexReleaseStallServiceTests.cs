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
    }

    private static MangaDexReleaseStallService CreateService(MangaHub.Infrastructure.Data.MangaHubDbContext db) =>
        new(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<MangaDexReleaseStallService>.Instance);
}
