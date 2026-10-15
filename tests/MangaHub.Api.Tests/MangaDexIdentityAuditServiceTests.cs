using MangaHub.Api.Services;
using MangaHub.Core.Models;
using MangaHub.Core.Services;

namespace MangaHub.Api.Tests;

public sealed class MangaDexIdentityAuditServiceTests
{
    [Fact]
    public async Task RunAsync_OpensHighPriorityIssueWhenMangaDexMalDoesNotMatchCatalog()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry
        {
            Title = "Correct catalog manga",
            MyAnimeListId = "100",
            MangaDexId = "wrong-mangadex-id"
        };
        db.MangaEntries.Add(manga);
        await db.SaveChangesAsync();
        var mangaDex = new FakeMangaDexSource
        {
            Identity = new MangaDexCatalogIdentity("wrong-mangadex-id", "Different series", "200", [])
        };

        var result = await CreateService(db, mangaDex).RunAsync(CancellationToken.None);

        var issue = Assert.Single(db.AdminIssues);
        Assert.Equal(AdminIssueTypes.MangaDexIdentityMismatch, issue.Kind);
        Assert.Equal("high", issue.Priority);
        Assert.Contains("\"expectedMyAnimeListId\":\"100\"", issue.MetadataJson);
        Assert.Contains("\"linkedMyAnimeListId\":\"200\"", issue.MetadataJson);
        Assert.Equal(1, result.OpenedOrUpdatedIssues);
    }

    [Fact]
    public async Task RunAsync_ResolvesExistingIssueWhenTheCorrectIdentityIsRestored()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry { Title = "Recovered", MyAnimeListId = "100", MangaDexId = "correct-mangadex-id" };
        var issue = new AdminIssue
        {
            Kind = AdminIssueTypes.MangaDexIdentityMismatch,
            SubjectType = AdminIssueTypes.CatalogManga,
            SubjectId = manga.Id,
            TitleSnapshot = "MangaDex identity mismatch: Recovered",
            Status = "open"
        };
        db.AddRange(manga, issue);
        await db.SaveChangesAsync();
        var mangaDex = new FakeMangaDexSource
        {
            Identity = new MangaDexCatalogIdentity("correct-mangadex-id", "Recovered", "100", [])
        };

        var result = await CreateService(db, mangaDex).RunAsync(CancellationToken.None);

        Assert.Equal("resolved", issue.Status);
        Assert.Equal(1, result.ResolvedIssues);
    }

    [Fact]
    public async Task RunAsync_DoesNotFlagWhenMangaDexDoesNotExposeAMalRelation()
    {
        await using var db = TestDb.Create();
        db.MangaEntries.Add(new MangaEntry { Title = "No remote MAL", MyAnimeListId = "100", MangaDexId = "mangadex-id" });
        await db.SaveChangesAsync();
        var mangaDex = new FakeMangaDexSource
        {
            Identity = new MangaDexCatalogIdentity("mangadex-id", "No remote MAL", "", [])
        };

        var result = await CreateService(db, mangaDex).RunAsync(CancellationToken.None);

        Assert.Empty(db.AdminIssues);
        Assert.Equal(0, result.AuditedEntries);
    }

    private static MangaDexIdentityAuditService CreateService(MangaHub.Infrastructure.Data.MangaHubDbContext db, FakeMangaDexSource mangaDex) =>
        new(db, mangaDex, Microsoft.Extensions.Logging.Abstractions.NullLogger<MangaDexIdentityAuditService>.Instance);
}
