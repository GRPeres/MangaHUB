using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;

namespace MangaHub.Api.Tests;

public sealed class ShelfServiceTests
{
    [Fact]
    public async Task AddAsync_CreatesShelfEntryWithNormalizedStatusAndCatalogDefaults()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var manga = new MangaEntry
        {
            Title = "Berserk",
            Category = "Dark fantasy",
            Description = "A grim journey.",
            MangaDexId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
        };
        db.MangaEntries.Add(manga);
        await db.SaveChangesAsync();
        var service = CreateShelfService(db);

        var result = await service.AddAsync(userId, new AddToShelfRequest(manga.Id, "completed", "376", 99, "", "", "peak"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("done", result.ReadingStatus);
        Assert.Equal("376", result.CurrentChapter);
        Assert.True(result.IsRead);
        Assert.Equal(5, result.Score);
        Assert.Equal("Dark fantasy", result.Category);
        Assert.Equal("A grim journey.", result.Summary);
        Assert.Equal("peak", result.Notes);
    }

    [Fact]
    public async Task AddAsync_UpdatesExistingShelfEntryWithoutDuplicating()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var manga = new MangaEntry { Title = "Dandadan" };
        db.MangaEntries.Add(manga);
        await db.SaveChangesAsync();
        var service = CreateShelfService(db);

        await service.AddAsync(userId, new AddToShelfRequest(manga.Id, "planned", "", null, "", "", ""), CancellationToken.None);
        await service.AddAsync(userId, new AddToShelfRequest(manga.Id, "reading", "12", null, "", "", ""), CancellationToken.None);

        Assert.Single(db.UserMangaEntries);
        Assert.Equal("reading", db.UserMangaEntries.Single().ReadingStatus);
        Assert.Equal("12", db.UserMangaEntries.Single().CurrentChapter);
    }

    [Fact]
    public async Task UpdateAsync_MarkingAnEntryDoneMarksItsCurrentChapterRead()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var manga = new MangaEntry { Title = "Witch Hat Atelier" };
        db.MangaEntries.Add(manga);
        db.UserMangaEntries.Add(new UserMangaEntry
        {
            UserId = userId,
            MangaEntry = manga,
            ReadingStatus = "reading",
            CurrentChapter = "10",
            IsRead = false
        });
        await db.SaveChangesAsync();
        var service = CreateShelfService(db);

        var result = await service.UpdateAsync(userId, manga.Id,
            new AddToShelfRequest(manga.Id, "done", "12", null, "", "", ""), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("done", result.ReadingStatus);
        Assert.Equal("12", result.CurrentChapter);
        Assert.True(result.IsRead);
    }

    [Fact]
    public async Task ResolveShelfUserIdAsync_AllowsAdminsOnlyForOtherUsers()
    {
        await using var db = TestDb.Create();
        var target = new MangaUser { Username = "target", PasswordHash = "hash" };
        var normal = new MangaUser { Username = "normal", PasswordHash = "hash", Role = "user" };
        var admin = new MangaUser { Username = "admin", PasswordHash = "hash", Role = "admin" };
        db.Users.AddRange(target, normal, admin);
        await db.SaveChangesAsync();
        var service = CreateShelfService(db);

        var denied = await service.ResolveShelfUserIdAsync(normal, target.Id, CancellationToken.None);
        var allowed = await service.ResolveShelfUserIdAsync(admin, target.Id, CancellationToken.None);

        Assert.Null(denied);
        Assert.Equal(target.Id, allowed);
    }

    [Fact]
    public async Task ImportAsync_CreatesCatalogAndShelfRowsForAdminImport()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var service = CreateShelfService(db);
        var csv = """
            Name,Link,Status,Chapter,Rating,Type,Summary,Notes
            Berserk,https://mangadex.org/title/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/berserk,Completed,376,5,Seinen,A grim journey,classic
            """;

        var result = await service.ImportAsync(userId, canCreateCatalog: true, new ShelfImportRequest(csv, true), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.CreatedCatalogEntries);
        Assert.Single(db.MangaEntries);
        Assert.Single(db.UserMangaEntries);
        Assert.Equal("done", db.UserMangaEntries.Single().ReadingStatus);
        Assert.True(db.UserMangaEntries.Single().IsRead);
    }

    [Fact]
    public async Task ImportAsync_SkipsMissingCatalogForNormalUser()
    {
        await using var db = TestDb.Create();
        var service = CreateShelfService(db);
        var csv = """
            Name,Link
            Berserk,https://mangadex.org/title/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/berserk
            """;

        var result = await service.ImportAsync(Guid.NewGuid(), canCreateCatalog: false, new ShelfImportRequest(csv, true), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Contains(result.Messages, message => message.Contains("only admins", StringComparison.OrdinalIgnoreCase));
    }

    private static ShelfService CreateShelfService(MangaHub.Infrastructure.Data.MangaHubDbContext db) =>
        new(new ShelfRepository(db), new CatalogRepository(db), new UserRepository(db));
}
