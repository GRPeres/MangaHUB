using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;

namespace MangaHub.Api.Tests;

public sealed class IssueReportingServiceTests
{
    [Fact]
    public async Task Discovery_WithSharedMalAndMangaDexIds_CreatesOneIssuePerPair()
    {
        await using var db = TestDb.Create();
        db.MangaEntries.AddRange(
            new MangaEntry { Title = "First", MyAnimeListId = "178093", MangaDexId = "b6b89f54-81c1-4e7e-ae80-b4dccdd63ada" },
            new MangaEntry { Title = "First duplicate", MyAnimeListId = "178093", MangaDexId = "b6b89f54-81c1-4e7e-ae80-b4dccdd63ada" },
            new MangaEntry { Title = "Second", MyAnimeListId = "187686", MangaDexId = "a53f4d4e-91fb-4242-b328-637fb32d0729" },
            new MangaEntry { Title = "Second duplicate", MyAnimeListId = "187686", MangaDexId = "a53f4d4e-91fb-4242-b328-637fb32d0729" });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var results = await service.ListAsync("open", 0, 40, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal(2, results.Select(issue => issue.SubjectId).Distinct().Count());
        Assert.Equal(2, await service.CountOpenAsync(CancellationToken.None));
        Assert.Equal(2, db.AdminIssues.Count());
    }

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

    [Fact]
    public async Task ReintegrateWithMangaDexAsync_ReplacesTheBrokenExternalReaderAndResolvesTheIssue()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry
        {
            Title = "Repair Me",
            MyAnimeListId = "1234",
            FallbackReaderUrl = "https://old.example/title/repair",
            ReaderPreference = global::MangaHub.Core.Models.ReaderPreference.External
        };
        var reporter = new MangaUser { Username = "reader", PasswordHash = "hash" };
        db.MangaEntries.Add(manga);
        db.Users.Add(reporter);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = reporter.Id, MangaEntryId = manga.Id, ReadingStatus = "reading" });
        await db.SaveChangesAsync();
        var mangaDex = new FakeMangaDexSource();
        mangaDex.CatalogMatches.Add(new MangaDexCatalogMatch("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Repair Me"));
        var service = CreateService(db, mangaDex);
        var report = await service.ReportAsync(reporter.Id, new(AdminIssueTypes.ExternalReaderLink, AdminIssueTypes.CatalogManga, manga.Id, "broken"), CancellationToken.None);

        var match = await service.ReintegrateWithMangaDexAsync(Guid.NewGuid(), report!.IssueId, CancellationToken.None);

        Assert.NotNull(match);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", manga.MangaDexId);
        Assert.Equal(ReaderPreference.MangaHub, manga.ReaderPreference);
        Assert.Empty(manga.FallbackReaderUrl);
        Assert.Equal("resolved", db.AdminIssues.Single().Status);
        Assert.Single(db.Notifications);
    }

    [Fact]
    public async Task ReintegrateWithSelectedMetadataAsync_AssignsMalMetadataAndRestoresTheReader()
    {
        await using var db = TestDb.Create();
        var manga = new MangaEntry
        {
            Title = "Unknown external title",
            FallbackReaderUrl = "https://old.example/title/repair",
            ReaderPreference = ReaderPreference.External
        };
        var reporter = new MangaUser { Username = "reader", PasswordHash = "hash" };
        db.MangaEntries.Add(manga);
        db.Users.Add(reporter);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = reporter.Id, MangaEntryId = manga.Id, ReadingStatus = "reading" });
        await db.SaveChangesAsync();
        var mangaDex = new FakeMangaDexSource();
        mangaDex.CatalogMatches.Add(new MangaDexCatalogMatch("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Correct MAL Title"));
        var service = CreateService(db, mangaDex);
        var report = await service.ReportAsync(reporter.Id, new(AdminIssueTypes.ExternalReaderLink, AdminIssueTypes.CatalogManga, manga.Id, "broken"), CancellationToken.None);

        var result = await service.ReintegrateWithSelectedMetadataAsync(Guid.NewGuid(), report!.IssueId,
            new("1234", "Correct MAL Title", "Author", "https://covers.example/cover.jpg", 2024, "Manga", "Summary", "manga", "ongoing", 12, 2),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.MetadataAssigned);
        Assert.True(result.ReaderRestored);
        Assert.Equal("1234", manga.MyAnimeListId);
        Assert.Equal("Correct MAL Title", manga.Title);
        Assert.Equal("myanimelist", manga.MetadataSource);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", manga.MangaDexId);
        Assert.Empty(manga.FallbackReaderUrl);
        Assert.Equal("resolved", db.AdminIssues.Single().Status);
    }

    [Fact]
    public async Task MergeDuplicateCatalogIssueAsync_MovesShelfDataAndRemovesTheDuplicate()
    {
        await using var db = TestDb.Create();
        var keep = new MangaEntry { Title = "Primary title", MyAnimeListId = "777" };
        var remove = new MangaEntry { Title = "Duplicate title", MyAnimeListId = "777" };
        var user = new MangaUser { Username = "reader", PasswordHash = "hash" };
        db.MangaEntries.AddRange(keep, remove);
        db.Users.Add(user);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = user.Id, MangaEntryId = keep.Id, CurrentChapter = "12", ReadingStatus = "reading", IsRead = false });
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = user.Id, MangaEntryId = remove.Id, CurrentChapter = "14", ReadingStatus = "reading", IsRead = true, Notes = "Imported note" });
        db.Notifications.Add(new MangaNotification { UserId = user.Id, MangaEntryId = remove.Id, Type = "new-chapter", ChapterNumber = 14, Language = "en", Title = "Duplicate title" });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var listed = await service.ListAsync("open", 0, 40, CancellationToken.None);
        var issue = Assert.Single(listed);
        var details = await service.GetDetailsAsync(issue.Id, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(2, details!.DuplicateCatalogEntries!.Count);
        var merged = await service.MergeDuplicateCatalogIssueAsync(Guid.NewGuid(), issue.Id, new(keep.Id), CancellationToken.None);

        Assert.True(merged);
        Assert.Single(db.MangaEntries);
        var shelf = Assert.Single(db.UserMangaEntries);
        Assert.Equal(keep.Id, shelf.MangaEntryId);
        Assert.Equal("14", shelf.CurrentChapter);
        Assert.True(shelf.IsRead);
        Assert.Contains("Imported note", shelf.Notes);
        Assert.Equal(keep.Id, Assert.Single(db.Notifications).MangaEntryId);
        Assert.Equal("resolved", db.AdminIssues.Single().Status);
    }

    private static IssueReportingService CreateService(MangaHub.Infrastructure.Data.MangaHubDbContext db, FakeMangaDexSource? mangaDex = null) =>
        new(
            new AdminIssueRepository(db),
            new ShelfRepository(db),
            new CatalogRepository(db),
            new NotificationRepository(db),
            new MangaDexCatalogMatchService(mangaDex ?? new FakeMangaDexSource()));
}
