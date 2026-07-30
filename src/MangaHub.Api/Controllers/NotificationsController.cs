using MangaHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController(CurrentUserService currentUsers, NotificationService notifications) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await notifications.ListAsync(user.Id, cancellationToken));
    }

    [HttpGet("unread-count")] public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await notifications.UnreadCountAsync(user.Id, cancellationToken));
    }

    [HttpPost("{notificationId:guid}/read")] public async Task<IActionResult> MarkRead(Guid notificationId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        return await notifications.MarkReadAsync(user.Id, notificationId, cancellationToken) ? NoContent() : NotFound();
    }
}
