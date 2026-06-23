using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;

namespace MangaHub.Api.Tests;

public sealed class ProgressServiceTests
{
    [Fact]
    public async Task SaveAsync_UpsertsProgressByUserAndSeries()
    {
        await using var db = TestDb.Create();
        var service = new ProgressService(new ProgressRepository(db));
        var userId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var firstChapter = Guid.NewGuid();
        var secondChapter = Guid.NewGuid();

        await service.SaveAsync(userId, new ProgressRequest(seriesId, firstChapter, 3), CancellationToken.None);
        await service.SaveAsync(userId, new ProgressRequest(seriesId, secondChapter, 9), CancellationToken.None);

        Assert.Single(db.ReadingProgress);
        var progress = Assert.Single(await service.ListAsync(userId, CancellationToken.None));
        Assert.Equal(secondChapter, progress.ChapterId);
        Assert.Equal(9, progress.Page);
    }
}
