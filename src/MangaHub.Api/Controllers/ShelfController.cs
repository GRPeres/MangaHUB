using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/shelf")]
public sealed class ShelfController(CurrentUserService currentUsers, ShelfService shelf) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddToShelfRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var entry = await shelf.AddAsync(user.Id, request, cancellationToken);
        return entry is null ? NotFound() : Created($"/api/manga/{entry.Id}", entry);
    }

    [HttpPut("{entryId:guid}")]
    public async Task<IActionResult> Update(Guid entryId, [FromQuery] Guid? userId, [FromBody] AddToShelfRequest request, CancellationToken cancellationToken)
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

        var updated = await shelf.UpdateAsync(targetUserId.Value, entryId, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{entryId:guid}")]
    public async Task<IActionResult> Remove(Guid entryId, [FromQuery] Guid? userId, CancellationToken cancellationToken)
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

        var removed = await shelf.RemoveAsync(targetUserId.Value, entryId, cancellationToken);
        return removed ? NoContent() : NotFound();
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] ShelfImportRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var canCreateCatalog = CurrentUserService.IsAdmin(user) && request.CreateMissingCatalogEntries;
        var result = await shelf.ImportAsync(user.Id, canCreateCatalog, request, cancellationToken);
        return result is null ? BadRequest("CSV text is required.") : Ok(result);
    }
}
