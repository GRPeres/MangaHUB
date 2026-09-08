using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;

namespace MangaHub.Api.Tests;

public sealed class IssueReportingServiceTests
{
    [Fact]
    public void Registry_OnlyAllowsKnownIssueSubjectPairs()
    {
        Assert.True(AdminIssueTypes.Supports(AdminIssueTypes.ExternalReaderLink, AdminIssueTypes.CatalogManga));
        Assert.False(AdminIssueTypes.Supports(AdminIssueTypes.ExternalReaderLink, "shelf-entry"));
        Assert.False(AdminIssueTypes.Supports("unknown", AdminIssueTypes.CatalogManga));
    }

    [Fact]
    public async Task ReportAsync_MergesReportsIntoOneOpenIssue()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry { Title = "External Manga", FallbackReaderUrl = "https://reader.example/title/external" };
        var firstUser = new MangaUser { Username = "first", PasswordHash = "hash" };
        var secondUser = new MangaUser { Username = "second", PasswordHash = "hash" };
        db.MangaEntries.Add(manga);
        db.Users.AddRange(firstUser, secondUser);
        db.UserMangaEntries.AddRange(
            new UserMangaEntry { UserId = firstUser.Id, MangaEntryId = manga.Id, ReadingStatus = "reading" },
            new UserMangaEntry { UserId = secondUser.Id, MangaEntryId = manga.Id, ReadingStatus = "reading" });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var first = await service.ReportAsync(firstUser.Id, new(AdminIssueTypes.ExternalReaderLink, AdminIssueTypes.CatalogManga, manga.Id, "broken", "404"), CancellationToken.None);
        var second = await service.ReportAsync(secondUser.Id, new(AdminIssueTypes.ExternalReaderLink, AdminIssueTypes.CatalogManga, manga.Id, "obsolete", "Old host"), CancellationToken.None);
        var repeated = await service.ReportAsync(firstUser.Id, new(AdminIssueTypes.ExternalReaderLink, AdminIssueTypes.CatalogManga, manga.Id, "broken"), CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.IssueId, second!.IssueId);
        Assert.Equal(2, repeated!.ReportCount);
        Assert.Single(db.AdminIssues);
        Assert.Equal(2, db.AdminIssueReports.Count());
    }

    [Fact]
    public async Task ResolveExternalReaderLinkAsync_UpdatesCatalogAndNotifiesReporters()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry { Title = "Repair Me", FallbackReaderUrl = "https://old.example/title/repair" };
        var reporter = new MangaUser { Username = "reader", PasswordHash = "hash" };
        db.MangaEntries.Add(manga);
        db.Users.Add(reporter);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = reporter.Id, MangaEntryId = manga.Id, ReadingStatus = "reading" });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var report = await service.ReportAsync(reporter.Id, new(AdminIssueTypes.ExternalReaderLink, AdminIssueTypes.CatalogManga, manga.Id, "broken"), CancellationToken.None);

        var resolved = await service.ResolveExternalReaderLinkAsync(Guid.NewGuid(), report!.IssueId, new("https://new.example/title/repair", "Updated source"), CancellationToken.None);

        Assert.True(resolved);
        Assert.Equal("https://new.example/title/repair", manga.FallbackReaderUrl);
        Assert.Equal("resolved", db.AdminIssues.Single().Status);
        var notification = Assert.Single(db.Notifications);
        Assert.Equal(reporter.Id, notification.UserId);
        Assert.StartsWith("issue-", notification.Type, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportAsync_RejectsTrackedOrMissingExternalLinks()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry { Title = "Tracked", MangaDexId = "123", FallbackReaderUrl = "https://reader.example/title/tracked" };
        var user = new MangaUser { Username = "reader", PasswordHash = "hash" };
        db.MangaEntries.Add(manga);
        db.Users.Add(user);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = user.Id, MangaEntryId = manga.Id, ReadingStatus = "reading" });
        await db.SaveChangesAsync();

        var result = await CreateService(db).ReportAsync(user.Id, new(AdminIssueTypes.ExternalReaderLink, AdminIssueTypes.CatalogManga, manga.Id, "broken"), CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(db.AdminIssues);
    }

    private static IssueReportingService CreateService(MangaHub.Infrastructure.Data.MangaHubDbContext db) =>
        new(new AdminIssueRepository(db), new ShelfRepository(db), new CatalogRepository(db), new NotificationRepository(db));
}
