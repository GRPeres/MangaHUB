using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/progress")]
public sealed class ProgressController(CurrentUserService currentUsers, ProgressService progress) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] ProgressRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await progress.SaveAsync(user.Id, request, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await progress.ListAsync(user.Id, cancellationToken));
    }
}
