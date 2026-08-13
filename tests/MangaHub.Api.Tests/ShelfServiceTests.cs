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

    [Fact]
    public async Task ImportAsync_AcceptsTitleOnlyRowsWithAllOptionalColumnsMissing()
    {
        await using var db = TestDb.Create();
        var service = CreateShelfService(db);

        var result = await service.ImportAsync(Guid.NewGuid(), canCreateCatalog: true,
            new ShelfImportRequest("Title\nMinimal Manga\n", true), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.Imported);
        var shelf = Assert.Single(db.UserMangaEntries);
        Assert.Equal("planned", shelf.ReadingStatus);
        Assert.Equal("", shelf.CurrentChapter);
    }

    [Fact]
    public async Task ImportAsync_MapsRichMigrationColumnsAndMatchesByMangaDexId()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var service = CreateShelfService(db);
        var csv = """
            Title,MangaDex ID,MAL ID,MangaUpdates ID,Authors,Categories,Catalog Description,Cover URL,Format,First Published,Chapter Count,Volume Count,Publishing Status,Fallback Reader URL,Reader Preference,Status,Chapter,Rating,Personal Category,Summary,Notes,Current Chapter Read
            Rich Manga,aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa,100,200,An Author,"Action, Drama",Catalog copy,https://example.com/cover.jpg,Manhwa,2024,40,4,ongoing,https://reader.example/manga,hybrid,Reading,12,4,Favorites,Personal copy,Keep reading,false
            """;

        var result = await service.ImportAsync(userId, canCreateCatalog: true, new ShelfImportRequest(csv, true), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Imported == 1, string.Join(" ", result.Messages));
        var manga = Assert.Single(db.MangaEntries);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", manga.MangaDexId);
        Assert.Equal("100", manga.MyAnimeListId);
        Assert.Equal("200", manga.MangaUpdatesId);
        Assert.Equal("An Author", manga.Authors);
        Assert.Equal("Action, Drama", manga.Category);
        Assert.Equal("Catalog copy", manga.Description);
        Assert.Equal("Manhwa", manga.MediaType);
        Assert.Equal(2024, manga.FirstPublishYear);
        Assert.Equal(40, manga.ChapterCount);
        Assert.Equal(4, manga.VolumeCount);
        Assert.Equal("hybrid", manga.ReaderPreference);
        var shelf = Assert.Single(db.UserMangaEntries);
        Assert.Equal("reading", shelf.ReadingStatus);
        Assert.Equal("12", shelf.CurrentChapter);
        Assert.False(shelf.IsRead);
        Assert.Equal(4, shelf.Score);
        Assert.Equal("Favorites", shelf.Category);
        Assert.Equal("Personal copy", shelf.Summary);
        Assert.Equal("Keep reading", shelf.Notes);
    }

    [Fact]
    public async Task ImportAsync_PreservesExistingShelfValuesWhenTheirColumnsAreOmitted()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var manga = new MangaEntry { Title = "Existing Manga" };
        db.MangaEntries.Add(manga);
        db.UserMangaEntries.Add(new UserMangaEntry
        {
            UserId = userId,
            MangaEntry = manga,
            ReadingStatus = "reading",
            CurrentChapter = "12",
            Score = 5,
            Notes = "keep this"
        });
        await db.SaveChangesAsync();
        var service = CreateShelfService(db);

        var result = await service.ImportAsync(userId, canCreateCatalog: true,
            new ShelfImportRequest("Name\nExisting Manga\n", true), CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Imported == 1, string.Join(" ", result.Messages));
        var shelf = Assert.Single(db.UserMangaEntries);
        Assert.Equal("reading", shelf.ReadingStatus);
        Assert.Equal("12", shelf.CurrentChapter);
        Assert.Equal(5, shelf.Score);
        Assert.Equal("keep this", shelf.Notes);
    }

    [Fact]
    public async Task ImportAsync_UsesExplicitColumnMappingsForCustomSpreadsheetHeaders()
    {
        await using var db = TestDb.Create();
        var service = CreateShelfService(db);
        var mappings = new Dictionary<string, string>
        {
            ["title"] = "Comic name",
            ["currentchapter"] = "Last read",
            ["score"] = "My score"
        };

        var result = await service.ImportAsync(Guid.NewGuid(), canCreateCatalog: true,
            new ShelfImportRequest("Comic name,Last read,My score\nMapped Manga,24,5\n", true, mappings), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.Imported);
        var shelf = Assert.Single(db.UserMangaEntries);
        Assert.Equal("reading", shelf.ReadingStatus);
        Assert.Equal("24", shelf.CurrentChapter);
        Assert.Equal(5, shelf.Score);
    }

    [Fact]
    public async Task ImportAsync_RollsBackEveryRowWhenAnyMappedValueIsInvalid()
    {
        await using var db = TestDb.Create();
        var service = CreateShelfService(db);
        var mappings = new Dictionary<string, string> { ["title"] = "Manga", ["score"] = "Rating" };
        var csv = "Manga,Rating\nValid Manga,5\nInvalid Manga,excellent\n";

        var result = await service.ImportAsync(Guid.NewGuid(), canCreateCatalog: true,
            new ShelfImportRequest(csv, true, mappings), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(db.MangaEntries);
        Assert.Contains(result.Messages, message => message.Contains("Rating 'excellent'", StringComparison.Ordinal));
    }

    private static ShelfService CreateShelfService(MangaHub.Infrastructure.Data.MangaHubDbContext db) =>
        new(new ShelfRepository(db), new CatalogRepository(db), new UserRepository(db));
}
