using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Models;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Sources;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Tests;

public sealed class ReaderServiceTests
{
    [Fact]
    public async Task GetReadOptionsAsync_ReturnsMangaDexAndLocalOptionsForShelfEntry()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var series = new MangaSeries { Title = "Local Berserk", Source = "local", ExternalId = "berserk" };
        var chapter = new MangaChapter { Series = series, SourceId = "Berserk/001.cbz", ChapterNumber = "1", PageCount = 24 };
        var entry = new MangaEntry
        {
            Title = "Berserk",
            MangaDexUrl = "https://mangadex.org/title/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/berserk",
            LocalSeriesId = series.Id
        };
        db.Series.Add(series);
        db.Chapters.Add(chapter);
        db.MangaEntries.Add(entry);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = userId, MangaEntry = entry });
        await db.SaveChangesAsync();
        var service = CreateReaderService(db, new FakeArchiveReader(), "library");

        var options = await service.GetReadOptionsAsync(userId, entry.Id, CancellationToken.None);

        Assert.NotNull(options);
        Assert.True(options.HasMangaDex);
        Assert.True(options.HasLocal);
        Assert.Equal($"/reader/{chapter.Id}/24?entryId={entry.Id}&chapter=1&source=local", options.LocalReaderUrl);
    }

    [Fact]
    public async Task GetPageAsync_RejectsPathTraversalOutsideLibraryRoot()
    {
        await using var db = TestDb.Create();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        var series = new MangaSeries { Title = "Unsafe", Source = "local", ExternalId = "unsafe" };
        var chapter = new MangaChapter { Series = series, SourceId = "../outside.cbz", ChapterNumber = "1", PageCount = 1 };
        db.Series.Add(series);
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync();
        var archive = new FakeArchiveReader();
        var service = CreateReaderService(db, archive, root);

        var page = await service.GetPageAsync(chapter.Id, 0, CancellationToken.None);

        Assert.Null(page);
        Assert.Empty(archive.RequestedPaths);
    }

    [Fact]
    public async Task GetPageAsync_ReadsCachedMangaDexChapterFromCacheRoot()
    {
        await using var db = TestDb.Create();
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var series = new MangaSeries { Title = "Cached Berserk", Source = "mangadex-cache", ExternalId = "manga-id" };
        var chapter = new MangaChapter { Series = series, SourceId = "chapter-id", ChapterNumber = "1", PageCount = 2 };
        db.Series.Add(series);
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync();
        var archive = new FakeArchiveReader();
        var service = CreateReaderService(db, archive, root);

        var page = await service.GetPageAsync(chapter.Id, 0, CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal(Path.Combine(root, "mangadex", "manga-id", "chapter-id.cbz"), Assert.Single(archive.RequestedPaths));
    }

    [Fact]
    public async Task PrepareMangaDexChapterAsync_CachesAndOpensTheCurrentChapter()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var entry = new MangaEntry { Title = "Berserk", MangaDexId = "berserk-id" };
        db.MangaEntries.Add(entry);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = userId, MangaEntry = entry, CurrentChapter = "Ch. 1", ReadingStatus = "planned" });
        await db.SaveChangesAsync();

        var mangaDex = new FakeMangaDexSource();
        mangaDex.Chapters.AddRange([
            new MangaHub.Core.Sources.MangaSourceChapter("chapter-1", "1", "Beginning", 20),
            new MangaHub.Core.Sources.MangaSourceChapter("chapter-2", "2", "The next step", 18)
        ]);
        mangaDex.Pages["chapter-1"] = [
            new MangaHub.Core.Sources.MangaPage(0, "https://uploads.mangadex.org/data/hash/001.jpg"),
            new MangaHub.Core.Sources.MangaPage(1, "https://uploads.mangadex.org/data/hash/002.jpg")
        ];
        var cache = new FakeMangaDexChapterCache();
        var service = CreateReaderService(db, new FakeArchiveReader(), "library", mangaDex, cache);

        var launch = await service.PrepareMangaDexChapterAsync(userId, entry.Id, null, null, CancellationToken.None);

        Assert.NotNull(launch);
        Assert.Equal("1", launch.CurrentChapter);
        Assert.Equal(2, launch.PageCount);
        Assert.Contains(entry.Id.ToString(), launch.ReaderUrl);
        Assert.Contains("chapter=1", launch.ReaderUrl);
        Assert.Equal(["chapter-1"], cache.CachedChapterIds);
        var shelf = await new ShelfRepository(db).GetAsync(userId, entry.Id, CancellationToken.None);
        Assert.Equal("reading", shelf!.ReadingStatus);
    }

    [Fact]
    public async Task PrepareMangaDexChapterAsync_NextChapterAdvancesProgress_ButPreviousChapterDoesNotRegressIt()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var entry = new MangaEntry { Title = "Berserk", MangaDexId = "berserk-id" };
        db.MangaEntries.Add(entry);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = userId, MangaEntry = entry, ReadingStatus = "planned" });
        await db.SaveChangesAsync();

        var mangaDex = new FakeMangaDexSource();
        mangaDex.Chapters.AddRange([
            new MangaHub.Core.Sources.MangaSourceChapter("chapter-1", "1", "Beginning", 20),
            new MangaHub.Core.Sources.MangaSourceChapter("chapter-2", "2", "The next step", 18)
        ]);
        mangaDex.Pages["chapter-1"] = [new MangaHub.Core.Sources.MangaPage(0, "https://uploads.mangadex.org/data/hash/001.jpg")];
        mangaDex.Pages["chapter-2"] = [new MangaHub.Core.Sources.MangaPage(0, "https://uploads.mangadex.org/data/hash/002.jpg")];
        var cache = new FakeMangaDexChapterCache();
        var service = CreateReaderService(db, new FakeArchiveReader(), "library", mangaDex, cache);

        var firstLaunch = await service.PrepareMangaDexChapterAsync(userId, entry.Id, null, null, CancellationToken.None);
        Assert.NotNull(firstLaunch);
        var cachedSeries = await new SeriesRepository(db).GetBySourceAndExternalIdAsync("mangadex-cache", "berserk-id", CancellationToken.None);
        var firstChapter = Assert.Single(cachedSeries!.Chapters);
        var nextLaunch = await service.PrepareMangaDexChapterAsync(userId, entry.Id, firstChapter.Id, null, CancellationToken.None);

        Assert.NotNull(nextLaunch);
        Assert.Equal("2", nextLaunch.CurrentChapter);
        Assert.Equal(["chapter-1", "chapter-2"], cache.CachedChapterIds);
        var shelf = await new ShelfRepository(db).GetAsync(userId, entry.Id, CancellationToken.None);
        Assert.Equal("2", shelf!.CurrentChapter);
        Assert.Equal("reading", shelf.ReadingStatus);

        var secondChapter = cachedSeries.Chapters.Single(chapter => chapter.SourceId == "chapter-2");
        var previousLaunch = await service.PrepareMangaDexChapterAsync(userId, entry.Id, null, secondChapter.Id, CancellationToken.None);

        Assert.NotNull(previousLaunch);
        Assert.Equal("1", previousLaunch.CurrentChapter);
        Assert.Equal(["chapter-1", "chapter-2"], cache.CachedChapterIds);
        shelf = await new ShelfRepository(db).GetAsync(userId, entry.Id, CancellationToken.None);
        Assert.Equal("2", shelf!.CurrentChapter);
    }

    [Fact]
    public async Task PrefetchNextMangaDexChapterAsync_CachesNextChapterWithoutChangingReadingProgress()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var entry = new MangaEntry { Title = "Berserk", MangaDexId = "berserk-id" };
        db.MangaEntries.Add(entry);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = userId, MangaEntry = entry, CurrentChapter = "1", ReadingStatus = "reading" });
        await db.SaveChangesAsync();

        var mangaDex = new FakeMangaDexSource();
        mangaDex.Chapters.AddRange([
            new MangaHub.Core.Sources.MangaSourceChapter("chapter-1", "1", "Beginning", 20),
            new MangaHub.Core.Sources.MangaSourceChapter("chapter-2", "2", "The next step", 18)
        ]);
        mangaDex.Pages["chapter-1"] = [new MangaHub.Core.Sources.MangaPage(0, "https://uploads.mangadex.org/data/hash/001.jpg")];
        mangaDex.Pages["chapter-2"] = [new MangaHub.Core.Sources.MangaPage(0, "https://uploads.mangadex.org/data/hash/002.jpg")];
        var cache = new FakeMangaDexChapterCache();
        var service = CreateReaderService(db, new FakeArchiveReader(), "library", mangaDex, cache);

        var launch = await service.PrepareMangaDexChapterAsync(userId, entry.Id, null, null, CancellationToken.None);
        Assert.NotNull(launch);
        var cachedSeries = await new SeriesRepository(db).GetBySourceAndExternalIdAsync("mangadex-cache", "berserk-id", CancellationToken.None);
        var currentChapter = Assert.Single(cachedSeries!.Chapters);

        await service.PrefetchNextMangaDexChapterAsync(userId, entry.Id, currentChapter.Id, CancellationToken.None);

        Assert.Equal(["chapter-1", "chapter-2"], cache.CachedChapterIds);
        var shelf = await new ShelfRepository(db).GetAsync(userId, entry.Id, CancellationToken.None);
        Assert.Equal("1", shelf!.CurrentChapter);
        Assert.Equal("reading", shelf.ReadingStatus);
        cachedSeries = await new SeriesRepository(db).GetBySourceAndExternalIdAsync("mangadex-cache", "berserk-id", CancellationToken.None);
        Assert.Equal(2, cachedSeries!.Chapters.Count);
    }

    [Fact]
    public async Task MarkCurrentChapterReadAsync_MarksOnlyTheShelfCurrentChapterAsRead()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var entry = new MangaEntry { Title = "Berserk", MangaDexId = "berserk-id" };
        var series = new MangaSeries { Title = "Berserk", Source = "mangadex-cache", ExternalId = "berserk-id" };
        var chapter = new MangaChapter { Series = series, SourceId = "chapter-1", ChapterNumber = "1", PageCount = 20 };
        db.MangaEntries.Add(entry);
        db.Series.Add(series);
        db.Chapters.Add(chapter);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = userId, MangaEntry = entry, CurrentChapter = "1", ReadingStatus = "reading" });
        await db.SaveChangesAsync();
        var service = CreateReaderService(db, new FakeArchiveReader(), "library");

        var marked = await service.MarkCurrentChapterReadAsync(userId, entry.Id, chapter.Id, CancellationToken.None);

        Assert.True(marked);
        var shelf = await new ShelfRepository(db).GetAsync(userId, entry.Id, CancellationToken.None);
        Assert.True(shelf!.IsRead);
    }

    [Fact]
    public async Task PrepareMangaDexChapterAsync_WhenCurrentChapterIsRead_OpensTheNextChapterAndClearsReadFlag()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var entry = new MangaEntry { Title = "Berserk", MangaDexId = "berserk-id" };
        db.MangaEntries.Add(entry);
        db.UserMangaEntries.Add(new UserMangaEntry
        {
            UserId = userId,
            MangaEntry = entry,
            CurrentChapter = "1",
            IsRead = true,
            ReadingStatus = "reading"
        });
        await db.SaveChangesAsync();

        var mangaDex = new FakeMangaDexSource();
        mangaDex.Chapters.AddRange([
            new MangaHub.Core.Sources.MangaSourceChapter("chapter-1", "1", "Beginning", 20),
            new MangaHub.Core.Sources.MangaSourceChapter("chapter-2", "2", "The next step", 18)
        ]);
        mangaDex.Pages["chapter-2"] = [new MangaHub.Core.Sources.MangaPage(0, "https://uploads.mangadex.org/data/hash/002.jpg")];
        var service = CreateReaderService(db, new FakeArchiveReader(), "library", mangaDex, new FakeMangaDexChapterCache());

        var launch = await service.PrepareMangaDexChapterAsync(userId, entry.Id, null, null, CancellationToken.None);

        Assert.NotNull(launch);
        Assert.Equal("2", launch.CurrentChapter);
        var shelf = await new ShelfRepository(db).GetAsync(userId, entry.Id, CancellationToken.None);
        Assert.Equal("2", shelf!.CurrentChapter);
        Assert.False(shelf.IsRead);
    }

    [Fact]
    public async Task PrepareMangaDexChapterAsync_WhenCompletedCurrentChapterHasNoNextChapter_ReportsNoNextChapter()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var entry = new MangaEntry { Title = "Berserk", MangaDexId = "berserk-id" };
        db.MangaEntries.Add(entry);
        db.UserMangaEntries.Add(new UserMangaEntry
        {
            UserId = userId,
            MangaEntry = entry,
            CurrentChapter = "2",
            IsRead = true,
            ReadingStatus = "reading"
        });
        await db.SaveChangesAsync();

        var mangaDex = new FakeMangaDexSource();
        mangaDex.Chapters.AddRange([
            new MangaHub.Core.Sources.MangaSourceChapter("chapter-1", "1", "Beginning", 20),
            new MangaHub.Core.Sources.MangaSourceChapter("chapter-2", "2", "The end", 18)
        ]);
        var service = CreateReaderService(db, new FakeArchiveReader(), "library", mangaDex, new FakeMangaDexChapterCache());

        await Assert.ThrowsAsync<ReaderService.NoNextMangaDexChapterException>(
            () => service.PrepareMangaDexChapterAsync(userId, entry.Id, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task PrepareMangaDexChapterAsync_WhenCurrentChapterCannotBeMatched_DoesNotResetProgress()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var entry = new MangaEntry { Title = "Berserk", MangaDexId = "berserk-id" };
        db.MangaEntries.Add(entry);
        db.UserMangaEntries.Add(new UserMangaEntry
        {
            UserId = userId,
            MangaEntry = entry,
            CurrentChapter = "special chapter",
            ReadingStatus = "reading"
        });
        await db.SaveChangesAsync();

        var mangaDex = new FakeMangaDexSource();
        mangaDex.Chapters.Add(new MangaHub.Core.Sources.MangaSourceChapter("chapter-1", "1", "Beginning", 20));
        var service = CreateReaderService(db, new FakeArchiveReader(), "library", mangaDex, new FakeMangaDexChapterCache());

        var launch = await service.PrepareMangaDexChapterAsync(userId, entry.Id, null, null, CancellationToken.None);

        Assert.Null(launch);
        var shelf = await new ShelfRepository(db).GetAsync(userId, entry.Id, CancellationToken.None);
        Assert.Equal("special chapter", shelf!.CurrentChapter);
    }

    private static ReaderService CreateReaderService(
        MangaHub.Infrastructure.Data.MangaHubDbContext db,
        FakeArchiveReader archive,
        string libraryPath,
        FakeMangaDexSource? mangaDex = null,
        FakeMangaDexChapterCache? cache = null) =>
        new(
            new ShelfRepository(db),
            new SeriesRepository(db),
            archive,
            cache ?? new FakeMangaDexChapterCache(),
            Options.Create(new MangaHubOptions { LibraryPath = libraryPath, MangaDexCachePath = libraryPath }),
            new MangaSourceRegistry([mangaDex ?? new FakeMangaDexSource()]));
}
