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
        Assert.Equal($"/reader/{chapter.Id}/24", options.LocalReaderUrl);
        Assert.Equal($"/reader/mangadex/{entry.Id}", options.MangaDexReaderUrl);
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
    public async Task GetMangaDexReaderSessionAsync_ChoosesTheNextUnreadChapter()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var entry = new MangaEntry { Title = "Berserk", MangaDexId = "berserk-id" };
        db.MangaEntries.Add(entry);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = userId, MangaEntry = entry, CurrentChapter = "1" });
        await db.SaveChangesAsync();

        var mangaDex = new FakeMangaDexSource();
        mangaDex.Chapters.AddRange([
            new MangaHub.Core.Sources.MangaSourceChapter("chapter-1", "1", "Beginning", 20),
            new MangaHub.Core.Sources.MangaSourceChapter("chapter-2", "2", "The next step", 18)
        ]);
        mangaDex.Pages["chapter-2"] = [
            new MangaHub.Core.Sources.MangaPage(0, "https://uploads.mangadex.org/data/hash/001.jpg"),
            new MangaHub.Core.Sources.MangaPage(1, "https://uploads.mangadex.org/data/hash/002.jpg")
        ];
        var service = CreateReaderService(db, new FakeArchiveReader(), "library", mangaDex);

        var session = await service.GetMangaDexReaderSessionAsync(userId, entry.Id, null, CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal("chapter-2", session.SelectedChapter.Id);
        Assert.Equal("2", session.SelectedChapter.Number);
        Assert.Equal(2, session.SelectedChapter.PageCount);
    }

    [Fact]
    public async Task SaveMangaDexProgressAsync_CompletingChapterUpdatesShelfProgress()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var entry = new MangaEntry { Title = "Berserk", MangaDexId = "berserk-id" };
        db.MangaEntries.Add(entry);
        db.UserMangaEntries.Add(new UserMangaEntry { UserId = userId, MangaEntry = entry, ReadingStatus = "planned" });
        await db.SaveChangesAsync();

        var mangaDex = new FakeMangaDexSource();
        mangaDex.Chapters.Add(new MangaHub.Core.Sources.MangaSourceChapter("chapter-2", "2", "The next step", 18));
        var service = CreateReaderService(db, new FakeArchiveReader(), "library", mangaDex);

        var progress = await service.SaveMangaDexProgressAsync(
            userId,
            entry.Id,
            new MangaHub.Core.Dto.MangaDexReaderProgressRequest("chapter-2", 17, true),
            CancellationToken.None);

        Assert.NotNull(progress);
        Assert.Equal("2", progress.CurrentChapter);
        Assert.Equal("reading", progress.ReadingStatus);
        var shelf = await new ShelfRepository(db).GetAsync(userId, entry.Id, CancellationToken.None);
        Assert.Equal("2", shelf!.CurrentChapter);
        Assert.Equal("reading", shelf.ReadingStatus);
    }

    private static ReaderService CreateReaderService(
        MangaHub.Infrastructure.Data.MangaHubDbContext db,
        FakeArchiveReader archive,
        string libraryPath,
        FakeMangaDexSource? mangaDex = null) =>
        new(
            new ShelfRepository(db),
            new SeriesRepository(db),
            archive,
            Options.Create(new MangaHubOptions { LibraryPath = libraryPath }),
            new MangaSourceRegistry([mangaDex ?? new FakeMangaDexSource()]),
            new FakeHttpClientFactory());
}
