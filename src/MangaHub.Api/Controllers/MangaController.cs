using MangaHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/manga")]
public sealed class MangaController(CurrentUserService currentUsers, ShelfService shelf, ReaderService reader, ReaderPreparationService preparations) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] Guid? userId, [FromQuery] int offset = 0, [FromQuery] int limit = 500, CancellationToken cancellationToken = default)
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

        return Ok(await shelf.ListAsync(targetUserId.Value, status, Math.Max(offset, 0), Math.Clamp(limit, 1, 500), cancellationToken));
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
        CancellationToken cancellationToken,
        [FromQuery] string? language = null,
        [FromQuery] bool allowLanguageFallback = false,
        [FromQuery] bool allowChapterJump = false,
        [FromQuery] string? requestedChapter = null)
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

        return Accepted(preparations.Start(user.Id, entryId, afterCachedChapterId, beforeCachedChapterId, language ?? user.PreferredLanguage, allowLanguageFallback, allowChapterJump, requestedChapter));
    }

    [HttpGet("{entryId:guid}/mangadex-reader/languages")]
    public async Task<IActionResult> MangaDexLanguages(Guid entryId, CancellationToken cancellationToken)
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

        var languages = await reader.GetMangaDexLanguagesAsync(user.Id, entryId, cancellationToken);
        return languages is null ? NotFound() : Ok(languages);
    }

    [HttpPost("{entryId:guid}/mangadex-reader/prefetch-next")]
    public async Task<IActionResult> PrefetchNextMangaDexChapter(
        Guid entryId,
        [FromQuery] Guid afterCachedChapterId,
        CancellationToken cancellationToken,
        [FromQuery] string? language = null)
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

        preparations.PrefetchNext(user.Id, entryId, afterCachedChapterId, language ?? user.PreferredLanguage);
        return Accepted();
    }

    [HttpPost("{entryId:guid}/reader/current-chapter-read/{chapterId:guid}")]
    public async Task<IActionResult> MarkCurrentChapterRead(Guid entryId, Guid chapterId, CancellationToken cancellationToken)
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

        return Ok(await reader.MarkCurrentChapterReadAsync(user.Id, entryId, chapterId, cancellationToken));
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
