using MangaHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/read")]
public sealed class ReadController(CurrentUserService currentUsers, ReaderService reader) : ControllerBase
{
    [HttpGet("{chapterId:guid}/pages/{pageIndex:int}")]
    public async Task<IActionResult> Page(Guid chapterId, int pageIndex, CancellationToken cancellationToken)
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

        var page = await reader.GetPageAsync(chapterId, pageIndex, cancellationToken);
        if (page is null)
        {
            return NotFound();
        }

        // Reader URLs include a per-session version, so a private browser cache can safely
        // retain decoded pages without exposing authenticated content to shared caches.
        Response.Headers.CacheControl = "private, max-age=86400, immutable";
        Response.Headers.Vary = "Cookie";
        return File(page.Bytes, page.ContentType);
    }
}
