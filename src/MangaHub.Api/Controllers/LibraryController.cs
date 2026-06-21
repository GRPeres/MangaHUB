using MangaHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/library")]
public sealed class LibraryController(CurrentUserService currentUsers, LibraryService library) : ControllerBase
{
    [HttpPost("scan")]
    public async Task<IActionResult> Scan(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await library.ScanAsync(cancellationToken));
    }
}
