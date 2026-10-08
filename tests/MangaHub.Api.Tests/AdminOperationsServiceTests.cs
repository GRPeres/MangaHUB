using MangaHub.Api.Services;
using MangaHub.Core.Models;
using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Tests;

public sealed class AdminOperationsServiceTests
{
    [Fact]
    public async Task TestArchiveRecoveryAsync_RestoresReadsAndRearchivesOneArchivedDataSaverChapter()
    {
        await using var db = TestDb.Create();
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"mangahub-archive-test-{Guid.NewGuid():N}");
        var series = new MangaSeries { Title = "Archived manga", Source = "mangadex-cache", ExternalId = "manga-id" };
        var chapter = new MangaChapter { Series = series, SourceId = "chapter-id", ChapterNumber = "7", ImageQuality = "data-saver" };
        db.Series.Add(series);
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync();

        var archivedPath = Path.Combine(cacheRoot, "archive", "mangadex", "data-saver", "manga-id", "chapter-id.cbz");
        Directory.CreateDirectory(Path.GetDirectoryName(archivedPath)!);
        await File.WriteAllBytesAsync(archivedPath, [1]);
        var cache = new FakeMangaDexChapterCache { RestoreArchivedResult = true, ArchiveResult = true };
        var service = new AdminOperationsService(
            db,
            Options.Create(new MangaHubOptions { MangaDexCachePath = cacheRoot }),
            cache,
            new FakeArchiveReader());

        try
        {
            var result = await service.TestArchiveRecoveryAsync(CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("Archived manga", result.Title);
            Assert.Equal("7", result.ChapterNumber);
            Assert.Equal(["chapter-id"], cache.RestoredChapterIds);
            Assert.Equal(["chapter-id"], cache.ArchivedChapterIds);
            Assert.Contains(db.ArchiveRecoveryEvents, item => item.Action == "test-restored" && item.ChapterSourceId == "chapter-id");
        }
        finally
        {
            Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ListHistoryAsync_AppliesTypeStatusAndTriggerFiltersBeforePaging()
    {
        await using var db = TestDb.Create();
        db.MaintenanceJobs.AddRange(
            new MaintenanceJob { Type = "release-sync", Status = "completed", Trigger = "scheduled", RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-2) },
            new MaintenanceJob { Type = "release-sync", Status = "failed", Trigger = "manual", RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
            new MaintenanceJob { Type = "library-scan", Status = "completed", Trigger = "scheduled", RequestedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var service = new AdminOperationsService(db, Options.Create(new MangaHubOptions()));

        var history = await service.ListHistoryAsync(0, 25, " RELEASE-SYNC ", "COMPLETED", "SCHEDULED", CancellationToken.None);

        var job = Assert.Single(history);
        Assert.Equal("release-sync", job.Type);
        Assert.Equal("completed", job.Status);
        Assert.Equal("scheduled", job.Trigger);
    }
}
