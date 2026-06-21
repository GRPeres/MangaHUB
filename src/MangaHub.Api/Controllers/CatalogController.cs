using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/catalog")]
public sealed class CatalogController(CurrentUserService currentUsers, CatalogService catalog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null
            ? Unauthorized()
            : Ok(await catalog.SearchAsync(user.Id, q, cancellationToken));
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
}
