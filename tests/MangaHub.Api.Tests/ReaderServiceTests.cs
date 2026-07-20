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
        Assert.Equal($"/reader/{chapter.Id}/24?entryId={entry.Id}&chapter=1", options.LocalReaderUrl);
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
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = userId, MangaEntry = entry, CurrentChapter = "1", ReadingStatus = "planned" });
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

        var launch = await service.PrepareMangaDexChapterAsync(userId, entry.Id, null, CancellationToken.None);

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
    public async Task PrepareMangaDexChapterAsync_NextChapterUpdatesCurrentChapter()
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

        var firstLaunch = await service.PrepareMangaDexChapterAsync(userId, entry.Id, null, CancellationToken.None);
        Assert.NotNull(firstLaunch);
        var cachedSeries = await new SeriesRepository(db).GetBySourceAndExternalIdAsync("mangadex-cache", "berserk-id", CancellationToken.None);
        var firstChapter = Assert.Single(cachedSeries!.Chapters);
        var nextLaunch = await service.PrepareMangaDexChapterAsync(userId, entry.Id, firstChapter.Id, CancellationToken.None);

        Assert.NotNull(nextLaunch);
        Assert.Equal("2", nextLaunch.CurrentChapter);
        Assert.Equal(["chapter-1", "chapter-2"], cache.CachedChapterIds);
        var shelf = await new ShelfRepository(db).GetAsync(userId, entry.Id, CancellationToken.None);
        Assert.Equal("2", shelf!.CurrentChapter);
        Assert.Equal("reading", shelf.ReadingStatus);
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
