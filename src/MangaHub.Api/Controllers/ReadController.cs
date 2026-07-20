using MangaHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/read")]
public sealed class ReadController(CurrentUserService currentUsers, ReaderService reader) : ControllerBase
{
    [HttpGet("mangadex/{entryId:guid}/{chapterId}/pages/{pageIndex:int}")]
    public async Task<IActionResult> MangaDexPage(Guid entryId, string chapterId, int pageIndex, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var page = await reader.GetMangaDexPageAsync(user.Id, entryId, chapterId, pageIndex, cancellationToken);
        return page is null ? NotFound() : File(page.Bytes, page.ContentType);
    }

    [HttpGet("{chapterId:guid}/pages/{pageIndex:int}")]
    public async Task<IActionResult> Page(Guid chapterId, int pageIndex, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var page = await reader.GetPageAsync(chapterId, pageIndex, cancellationToken);
        return page is null ? NotFound() : File(page.Bytes, page.ContentType);
    }
}
