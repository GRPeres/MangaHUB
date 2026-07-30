using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;

namespace MangaHub.Api.Services;

public sealed class NotificationService(NotificationRepository notifications)
{
    public Task<List<MangaNotificationResponse>> ListAsync(Guid userId, CancellationToken cancellationToken) => notifications.ListAsync(userId, cancellationToken);
    public Task<int> UnreadCountAsync(Guid userId, CancellationToken cancellationToken) => notifications.UnreadCountAsync(userId, cancellationToken);

    public async Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken)
    {
        var notification = await notifications.GetAsync(userId, notificationId, cancellationToken);
        if (notification is null) return false;
        notification.ReadAt ??= DateTimeOffset.UtcNow;
        await notifications.SaveChangesAsync(cancellationToken);
        return true;
    }
}
