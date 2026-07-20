using MangaHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/manga")]
public sealed class MangaController(CurrentUserService currentUsers, ShelfService shelf, ReaderService reader, ReaderPreparationService preparations) : ControllerBase
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
        if (!CurrentUserService.IsAdmin(user))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var options = await reader.GetReadOptionsAsync(user.Id, entryId, cancellationToken);
        return options is null ? NotFound() : Ok(options);
    }

    [HttpPost("{entryId:guid}/mangadex-reader/prepare")]
    public async Task<IActionResult> PrepareMangaDexChapter(
        Guid entryId,
        [FromQuery] Guid? afterCachedChapterId,
        [FromQuery] Guid? beforeCachedChapterId,
        CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }
        if (!CurrentUserService.IsAdmin(user))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return Accepted(preparations.Start(user.Id, entryId, afterCachedChapterId, beforeCachedChapterId));
    }

    [HttpGet("mangadex-reader/jobs/{jobId:guid}")]
    public async Task<IActionResult> GetMangaDexPreparation(Guid jobId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }
        if (!CurrentUserService.IsAdmin(user))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var preparation = preparations.Get(jobId, user.Id);
        return preparation is null ? NotFound() : Ok(preparation);
    }
}
