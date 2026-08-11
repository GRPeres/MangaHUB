using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Tests;

public sealed class UsageTrackingServiceTests
{
    [Fact]
    public async Task TrackAsync_DoesNothingUntilUserOptsIn()
    {
        await using var db = TestDb.Create();
        var user = new MangaUser { Username = "reader" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = new UsageTrackingService(new UsageRepository(db));

        await service.TrackAsync(user.Id, UsageEventTypes.ShelfAdded, Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(db.UsageEvents);
    }

    [Fact]
    public async Task TrackAsync_UsesIdempotencyKeyForChapterCompletion()
    {
        await using var db = TestDb.Create();
        var user = new MangaUser { Username = "reader", UsageAnalyticsEnabled = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = new UsageTrackingService(new UsageRepository(db));
        var entryId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();

        await service.TrackAsync(user.Id, UsageEventTypes.ChapterCompleted, entryId, chapterId, "", "chapter:1", null, CancellationToken.None);
        await service.TrackAsync(user.Id, UsageEventTypes.ChapterCompleted, entryId, chapterId, "", "chapter:1", null, CancellationToken.None);

        Assert.Single(db.UsageEvents);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheRequestingUsersAnalytics()
    {
        await using var db = TestDb.Create();
        var first = new MangaUser { Username = "first", UsageAnalyticsEnabled = true };
        var second = new MangaUser { Username = "second", UsageAnalyticsEnabled = true };
        db.Users.AddRange(first, second);
        await db.SaveChangesAsync();
        var service = new UsageTrackingService(new UsageRepository(db));
        await service.TrackAsync(first.Id, UsageEventTypes.Search, null, CancellationToken.None);
        await service.TrackAsync(second.Id, UsageEventTypes.Search, null, CancellationToken.None);

        await service.DeleteAsync(first.Id, CancellationToken.None);

        Assert.Empty(await db.UsageEvents.Where(item => item.UserId == first.Id).ToListAsync());
        Assert.Single(await db.UsageEvents.Where(item => item.UserId == second.Id).ToListAsync());
        Assert.Equal(2, await db.Users.CountAsync());
    }
}
