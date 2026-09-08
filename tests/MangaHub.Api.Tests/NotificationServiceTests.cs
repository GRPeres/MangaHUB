using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Models;

namespace MangaHub.Api.Tests;

public sealed class NotificationServiceTests
{
    [Fact]
    public async Task MarkReleaseNotificationsReadThroughAsync_MarksOnlyReachedChaptersForTheSameShelfEntry()
    {
        await using var db = TestDb.Create();
        var userId = Guid.NewGuid();
        var mangaEntryId = Guid.NewGuid();
        var reached = new MangaNotification { UserId = userId, MangaEntryId = mangaEntryId, Type = "new-chapter", ChapterNumber = 12m, Language = "en", Title = "Chapter 12", Body = "" };
        var later = new MangaNotification { UserId = userId, MangaEntryId = mangaEntryId, Type = "new-chapter", ChapterNumber = 13m, Language = "en", Title = "Chapter 13", Body = "" };
        var otherManga = new MangaNotification { UserId = userId, MangaEntryId = Guid.NewGuid(), Type = "new-chapter", ChapterNumber = 12m, Language = "en", Title = "Another manga", Body = "" };
        db.Notifications.AddRange(reached, later, otherManga);
        await db.SaveChangesAsync();

        var service = new NotificationService(new NotificationRepository(db));

        var marked = await service.MarkReleaseNotificationsReadThroughAsync(userId, mangaEntryId, "12", CancellationToken.None);

        Assert.Equal(1, marked);
        Assert.NotNull(reached.ReadAt);
        Assert.Null(later.ReadAt);
        Assert.Null(otherManga.ReadAt);
    }

    [Fact]
    public async Task MarkReleaseNotificationsReadThroughAsync_IgnoresUnparseableProgress()
    {
        await using var db = TestDb.Create();
        var notification = new MangaNotification { UserId = Guid.NewGuid(), MangaEntryId = Guid.NewGuid(), Type = "new-chapter", ChapterNumber = 12m, Language = "en", Title = "Chapter 12", Body = "" };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        var service = new NotificationService(new NotificationRepository(db));

        var marked = await service.MarkReleaseNotificationsReadThroughAsync(notification.UserId, notification.MangaEntryId, "special", CancellationToken.None);

        Assert.Equal(0, marked);
        Assert.Null(notification.ReadAt);
    }
}
