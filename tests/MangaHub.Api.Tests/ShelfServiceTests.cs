using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;

namespace MangaHub.Api.Tests;

public sealed class ShelfServiceTests
{
    [Fact]
    public async Task ListAsync_SectionsKeepUpdatesSeparateFromDoneAndSummarizeCounts()
    {
        await using var db = TestDb.Create();
        var user = new MangaUser { Username = "reader", PasswordHash = "hash", PreferredLanguage = "en" };
        var release = new MangaEntry { Title = "Fresh release", MangaDexId = "release-id" };
        var untracked = new MangaEntry { Title = "Needs tracking" };
        var planned = new MangaEntry { Title = "Plan next" };
        var done = new MangaEntry { Title = "Finished locally", MangaDexId = "done-id" };
        db.Users.Add(user);
        db.MangaEntries.AddRange(release, untracked, planned, done);
        await db.SaveChangesAsync();
        db.MangaDexLanguageLatestChapters.AddRange(
            new MangaDexLanguageLatestChapter { MangaEntryId = release.Id, Language = "en", LatestChapter = 12 },
            new MangaDexLanguageLatestChapter { MangaEntryId = done.Id, Language = "en", LatestChapter = 20 });
        db.UserMangaEntries.AddRange(
            new UserMangaEntry { UserId = user.Id, MangaEntryId = release.Id, ReadingStatus = "reading", CurrentChapter = "10", IsRead = true },
            new UserMangaEntry { UserId = user.Id, MangaEntryId = untracked.Id, ReadingStatus = "paused", CurrentChapter = "4", IsRead = true },
            new UserMangaEntry { UserId = user.Id, MangaEntryId = planned.Id, ReadingStatus = "planned" },
            new UserMangaEntry { UserId = user.Id, MangaEntryId = done.Id, ReadingStatus = "done", CurrentChapter = "10", IsRead = true });
        await db.SaveChangesAsync();
        var service = CreateShelfService(db);

        var updates = await service.ListAsync(user.Id, null, "updates", 0, 20, CancellationToken.None);
        var doneEntries = await service.ListAsync(user.Id, null, "done", 0, 20, CancellationToken.None);
        var summary = await service.GetSectionSummaryAsync(user.Id, CancellationToken.None);

        Assert.Equal(["Fresh release", "Needs tracking"], updates.Select(entry => entry.Title));
        Assert.Equal(["Finished locally"], doneEntries.Select(entry => entry.Title));
        Assert.Equal(2, summary.Updates);
        Assert.Equal(1, summary.NewReleases);
        Assert.Equal(1, summary.Untracked);
        Assert.Equal(1, summary.Planned);
        Assert.Equal(1, summary.Done);
        Assert.Equal(4, summary.All);
    }

    [Fact]
    public async Task ListAsync_UpdatesOnlyIncludeExternalEntriesWhoseWeeklyCheckIsDue()
    {
        await using var db = TestDb.Create();
        var user = new MangaUser { Username = "reader", PasswordHash = "hash" };
        var due = new MangaEntry { Title = "Due check" };
        var recent = new MangaEntry { Title = "Recently checked" };
        var planned = new MangaEntry { Title = "Planned check" };
        db.Users.Add(user);
        db.MangaEntries.AddRange(due, recent, planned);
        await db.SaveChangesAsync();
        db.UserMangaEntries.AddRange(
            new UserMangaEntry { UserId = user.Id, MangaEntryId = due.Id, ReadingStatus = "reading" },
            new UserMangaEntry { UserId = user.Id, MangaEntryId = recent.Id, ReadingStatus = "paused", LastExternalReaderVerifiedAt = DateTimeOffset.UtcNow.AddDays(-2) },
            new UserMangaEntry { UserId = user.Id, MangaEntryId = planned.Id, ReadingStatus = "planned" });
        await db.SaveChangesAsync();
        var service = CreateShelfService(db);

        var updates = await service.ListAsync(user.Id, null, "updates", 0, 20, CancellationToken.None);
        var summary = await service.GetSectionSummaryAsync(user.Id, CancellationToken.None);

        Assert.Equal(["Due check"], updates.Select(entry => entry.Title));
        Assert.True(updates.Single().IsManualReleaseCheckDue);
        Assert.Equal(1, summary.Untracked);
    }

    [Fact]
    public async Task ExternalReaderCheckIns_ArePerUserAndRequireAnEligibleEntry()
    {
        await using var db = TestDb.Create();
        var firstUser = new MangaUser { Username = "first", PasswordHash = "hash" };
        var secondUser = new MangaUser { Username = "second", PasswordHash = "hash" };
        var external = new MangaEntry { Title = "External", FallbackReaderUrl = "https://reader.example/title" };
        var tracked = new MangaEntry { Title = "Tracked", MangaDexId = "mangadex-id", FallbackReaderUrl = "https://reader.example/title" };
        db.Users.AddRange(firstUser, secondUser);
        db.MangaEntries.AddRange(external, tracked);
        await db.SaveChangesAsync();
        db.UserMangaEntries.AddRange(
            new UserMangaEntry { UserId = firstUser.Id, MangaEntryId = external.Id, ReadingStatus = "reading" },
            new UserMangaEntry { UserId = secondUser.Id, MangaEntryId = external.Id, ReadingStatus = "reading" },
            new UserMangaEntry { UserId = firstUser.Id, MangaEntryId = tracked.Id, ReadingStatus = "reading" });
        await db.SaveChangesAsync();
        var service = CreateShelfService(db);

        Assert.True(await service.RecordExternalReaderOpenedAsync(firstUser.Id, external.Id, CancellationToken.None));
        Assert.False(await service.RecordExternalReaderOpenedAsync(firstUser.Id, tracked.Id, CancellationToken.None));
        Assert.Single(await service.GetPendingExternalReaderCheckInsAsync(firstUser.Id, CancellationToken.None));
        Assert.Empty(await service.GetPendingExternalReaderCheckInsAsync(secondUser.Id, CancellationToken.None));

        Assert.True(await service.VerifyExternalReaderCheckAsync(firstUser.Id, external.Id, CancellationToken.None));
        var entry = db.UserMangaEntries.Single(item => item.UserId == firstUser.Id && item.MangaEntryId == external.Id);
        Assert.NotNull(entry.LastExternalReaderOpenedAt);
        Assert.NotNull(entry.LastExternalReaderVerifiedAt);
        Assert.Null(entry.ExternalReaderCheckPendingAt);
    }

    [Fact]
    public async Task ExportAsync_UsesTheRequestedShelfSection()
    {
        await using var db = TestDb.Create();
        var user = new MangaUser { Username = "exporter", PasswordHash = "hash" };
        var planned = new MangaEntry { Title = "Planned" };
        var reading = new MangaEntry { Title = "Reading" };
        db.Users.Add(user);
        db.MangaEntries.AddRange(planned, reading);
        await db.SaveChangesAsync();
        db.UserMangaEntries.AddRange(
            new UserMangaEntry { UserId = user.Id, MangaEntryId = planned.Id, ReadingStatus = "planned" },
            new UserMangaEntry { UserId = user.Id, MangaEntryId = reading.Id, ReadingStatus = "reading" });
        await db.SaveChangesAsync();
        var service = CreateShelfService(db);

        var plannedExport = await service.ExportAsync(user.Id, "planned", CancellationToken.None);
        var completeExport = await service.ExportAsync(user.Id, null, CancellationToken.None);

        Assert.Equal(["Planned"], plannedExport.Select(entry => entry.Title));
        Assert.Equal(2, completeExport.Count);
    }

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
