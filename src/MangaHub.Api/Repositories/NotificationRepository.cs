using MangaHub.Core.Dto;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class NotificationRepository(MangaHubDbContext db)
{
    public void Add(MangaHub.Core.Models.MangaNotification notification) => db.Notifications.Add(notification);
    public Task<List<MangaNotificationResponse>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Notifications.AsNoTracking().Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedAt).Take(50)
            .Select(notification => new MangaNotificationResponse(notification.Id, notification.MangaEntryId, notification.Type, notification.ChapterNumber, notification.Language, notification.Title, notification.Body, notification.CreatedAt, notification.ReadAt))
            .ToListAsync(cancellationToken);

    public Task<int> UnreadCountAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Notifications.CountAsync(notification => notification.UserId == userId && notification.ReadAt == null, cancellationToken);

    public Task<MangaHub.Core.Models.MangaNotification?> GetAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) =>
        db.Notifications.FirstOrDefaultAsync(notification => notification.UserId == userId && notification.Id == notificationId, cancellationToken);

    public Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Notifications.Where(notification => notification.UserId == userId && notification.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(notification => notification.ReadAt, DateTimeOffset.UtcNow), cancellationToken);

    public Task<int> ClearReadAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Notifications.Where(notification => notification.UserId == userId && notification.ReadAt != null)
            .ExecuteDeleteAsync(cancellationToken);

    public Task<List<MangaHub.Core.Models.MangaNotification>> GetUnreadReleaseNotificationsThroughAsync(Guid userId, Guid mangaEntryId, decimal throughChapter, CancellationToken cancellationToken) =>
        db.Notifications
            .Where(notification => notification.UserId == userId
                && notification.MangaEntryId == mangaEntryId
                && notification.ReadAt == null
                && notification.Type == "new-chapter"
                && notification.ChapterNumber <= throughChapter)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
