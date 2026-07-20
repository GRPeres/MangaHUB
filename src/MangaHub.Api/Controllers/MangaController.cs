using MangaHub.Core.Dto;
using MangaHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/manga")]
public sealed class MangaController(CurrentUserService currentUsers, ShelfService shelf, ReaderService reader) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] Guid? userId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var targetUserId = await shelf.ResolveShelfUserIdAsync(user, userId, cancellationToken);
        if (targetUserId is null)
        {
            return userId is null ? NotFound() : StatusCode(StatusCodes.Status403Forbidden);
        }

        return Ok(await shelf.ListAsync(targetUserId.Value, status, cancellationToken));
    }

    [HttpGet("{entryId:guid}/read-options")]
    public async Task<IActionResult> ReadOptions(Guid entryId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var options = await reader.GetReadOptionsAsync(user.Id, entryId, cancellationToken);
        return options is null ? NotFound() : Ok(options);
    }

    [HttpGet("{entryId:guid}/mangadex-reader")]
    public async Task<IActionResult> MangaDexReaderSession(Guid entryId, [FromQuery] string? chapterId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var session = await reader.GetMangaDexReaderSessionAsync(user.Id, entryId, chapterId, cancellationToken);
        return session is null ? NotFound() : Ok(session);
    }

    [HttpPost("{entryId:guid}/mangadex-reader/progress")]
    public async Task<IActionResult> SaveMangaDexProgress(
        Guid entryId,
        [FromBody] MangaDexReaderProgressRequest request,
        CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var progress = await reader.SaveMangaDexProgressAsync(user.Id, entryId, request, cancellationToken);
        return progress is null ? NotFound() : Ok(progress);
    }
}
