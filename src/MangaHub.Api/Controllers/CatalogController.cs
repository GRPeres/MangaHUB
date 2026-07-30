using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/catalog")]
public sealed class CatalogController(CurrentUserService currentUsers, CatalogService catalog, CatalogCacheService cache) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] string? language, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null
            ? Unauthorized()
            : Ok(await catalog.SearchAsync(user.Id, q, string.IsNullOrWhiteSpace(language) ? user.PreferredLanguage : language, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MangaEntryRequest request, CancellationToken cancellationToken)
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

        var created = await catalog.CreateAsync(user.Id, request, cancellationToken);
        return Created($"/api/catalog/{created.Id}", created);
    }

    [HttpPut("{entryId:guid}")]
    public async Task<IActionResult> Update(Guid entryId, [FromBody] MangaEntryRequest request, CancellationToken cancellationToken)
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

        var updated = await catalog.UpdateAsync(user.Id, entryId, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("{entryId:guid}/mangadex-cache")]
    public async Task<IActionResult> ListCache(Guid entryId, [FromQuery] string? language, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var result = await cache.ListAsync(entryId, language, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{entryId:guid}/mangadex-cache/download")]
    public async Task<IActionResult> DownloadCache(Guid entryId, [FromBody] CacheMangaDexChapterRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var result = await cache.DownloadAsync(entryId, request, string.IsNullOrWhiteSpace(request.Language) ? user.PreferredLanguage : request.Language, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{entryId:guid}/mangadex-cache/import")]
    [RequestSizeLimit(1024L * 1024 * 1024)]
    public async Task<IActionResult> ImportCache(Guid entryId, [FromForm] string chapterNumber, [FromForm] string? title, [FromForm] string? language, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var result = await cache.ImportAsync(entryId, chapterNumber, title, language, file, cancellationToken);
        return result is null ? BadRequest("Provide a MangaDex-linked catalog entry, chapter number, and non-empty .cbz file.") : Ok(result);
    }

    [HttpDelete("{entryId:guid}/mangadex-cache/{chapterId:guid}")]
    public async Task<IActionResult> DeleteCache(Guid entryId, Guid chapterId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return await cache.DeleteAsync(entryId, chapterId, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPut("{entryId:guid}/mangadex-cache/{chapterId:guid}")]
    public async Task<IActionResult> UpdateCache(Guid entryId, Guid chapterId, [FromBody] UpdateCachedMangaDexChapterRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var result = await cache.UpdateAsync(entryId, chapterId, request, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
