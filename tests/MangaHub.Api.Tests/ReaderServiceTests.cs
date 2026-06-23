using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Models;
using MangaHub.Infrastructure;
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

    private static ReaderService CreateReaderService(MangaHub.Infrastructure.Data.MangaHubDbContext db, FakeArchiveReader archive, string libraryPath) =>
        new(
            new ShelfRepository(db),
            new SeriesRepository(db),
            archive,
            Options.Create(new MangaHubOptions { LibraryPath = libraryPath }));
}
