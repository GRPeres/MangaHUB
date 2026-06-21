using MangaHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/series")]
public sealed class SeriesController(CurrentUserService currentUsers, SeriesService series) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? title, [FromQuery] string? source, [FromQuery] string? status, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await series.ListAsync(title, source, status, cancellationToken));
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await series.SearchAsync(q, cancellationToken));
    }

    [HttpGet("{seriesId:guid}")]
    public async Task<IActionResult> Get(Guid seriesId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var result = await series.GetAsync(seriesId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{seriesId:guid}/chapters")]
    public async Task<IActionResult> Chapters(Guid seriesId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await series.ListChaptersAsync(seriesId, cancellationToken));
    }
}
