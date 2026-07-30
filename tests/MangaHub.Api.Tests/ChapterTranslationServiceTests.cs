using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Models;
using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Tests;

public sealed class ChapterTranslationServiceTests
{
    [Fact]
    public async Task EnsureReadyAsync_SameLanguageUsesCanonicalArchiveWithoutDuplicate()
    {
        await using var db = TestDb.Create();
        var chapter = new MangaChapter
        {
            Series = new MangaSeries { Title = "Test", Source = "mangadex-cache", ExternalId = "manga-id" },
            SourceId = "chapter-id",
            SourceLanguage = "en",
            Language = "en",
            ChapterNumber = "1",
            PageCount = 12,
            FileHash = "source-hash"
        };
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync();
        var engine = new FakeChapterTranslationEngine();
        var service = CreateService(db, engine, Path.GetTempPath());

        var result = await service.EnsureReadyAsync(chapter, "manga-id", "en", CancellationToken.None);

        Assert.Equal(ChapterTranslationStatus.Ready, result.Status);
        Assert.Equal("", result.RelativePath);
        Assert.Equal("source-hash", result.FileHash);
        Assert.Empty(engine.Requests);
    }

    [Fact]
    public async Task EnsureReadyAsync_DifferentLanguageCreatesAndReusesTranslatedArchive()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mangahub-translation-test-{Guid.NewGuid():N}");
        try
        {
            await using var db = TestDb.Create();
            var chapter = new MangaChapter
            {
                Series = new MangaSeries { Title = "Test", Source = "mangadex-cache", ExternalId = "manga-id" },
                SourceId = "chapter-id",
                SourceLanguage = "ja",
                Language = "ja",
                ChapterNumber = "1",
                PageCount = 12,
                FileHash = "source-hash"
            };
            db.Chapters.Add(chapter);
            await db.SaveChangesAsync();
            var engine = new FakeChapterTranslationEngine();
            var service = CreateService(db, engine, root);

            var created = await service.EnsureReadyAsync(chapter, "manga-id", "pt-br", CancellationToken.None);
            var reused = await service.EnsureReadyAsync(chapter, "manga-id", "pt-br", CancellationToken.None);
            var archivePath = await service.GetReadableArchivePathAsync(chapter, "pt-br", CancellationToken.None);

            Assert.Equal(ChapterTranslationStatus.Ready, created.Status);
            Assert.Equal(created.Id, reused.Id);
            Assert.Single(engine.Requests);
            Assert.NotNull(archivePath);
            Assert.True(File.Exists(archivePath));
            Assert.Contains(Path.Combine("translations", "v3", chapter.Id.ToString("N")), archivePath);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static ChapterTranslationService CreateService(
        MangaHub.Infrastructure.Data.MangaHubDbContext db,
        FakeChapterTranslationEngine engine,
        string cacheRoot) =>
        new(
            new ChapterTranslationRepository(db),
            engine,
            Options.Create(new MangaHubOptions
            {
                MangaDexCachePath = cacheRoot,
                Translation = new ChapterTranslationOptions { Enabled = true }
            }));
}
