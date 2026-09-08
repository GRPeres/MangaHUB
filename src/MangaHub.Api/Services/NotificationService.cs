using System.Globalization;
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

    public Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken) => notifications.MarkAllReadAsync(userId, cancellationToken);
    public Task<int> ClearReadAsync(Guid userId, CancellationToken cancellationToken) => notifications.ClearReadAsync(userId, cancellationToken);

    public async Task<int> MarkReleaseNotificationsReadThroughAsync(Guid userId, Guid mangaEntryId, string chapterNumber, CancellationToken cancellationToken)
    {
        if (!TryParseChapterNumber(chapterNumber, out var chapter)) return 0;
        var pending = await notifications.GetUnreadReleaseNotificationsThroughAsync(userId, mangaEntryId, chapter, cancellationToken);
        if (pending.Count == 0) return 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var notification in pending)
        {
            notification.ReadAt = now;
        }
        await notifications.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }

    private static bool TryParseChapterNumber(string value, out decimal chapter)
    {
        var normalized = new string((value ?? "")
            .SkipWhile(character => !char.IsDigit(character))
            .TakeWhile(character => char.IsDigit(character) || character is '.' or ',')
            .ToArray())
            .Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out chapter);
    }
}
