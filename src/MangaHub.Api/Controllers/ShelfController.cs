using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/shelf")]
public sealed class ShelfController(CurrentUserService currentUsers, ShelfService shelf, ShelfExportService exports) : ControllerBase
{
    [HttpGet("{entryId:guid}")]
    public async Task<IActionResult> Get(Guid entryId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : (await shelf.GetAsync(user.Id, entryId, cancellationToken) is { } entry ? Ok(entry) : NotFound());
    }

    [HttpGet("external-reader/check-ins")]
    public async Task<IActionResult> PendingExternalReaderCheckIns(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await shelf.GetPendingExternalReaderCheckInsAsync(user.Id, cancellationToken));
    }

    [HttpPost("{entryId:guid}/external-reader/opened")]
    public async Task<IActionResult> RecordExternalReaderOpened(Guid entryId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        return await shelf.RecordExternalReaderOpenedAsync(user.Id, entryId, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("{entryId:guid}/external-reader/verified")]
    public async Task<IActionResult> VerifyExternalReaderCheck(Guid entryId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        return await shelf.VerifyExternalReaderCheckAsync(user.Id, entryId, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("{entryId:guid}/external-reader/dismiss")]
    public async Task<IActionResult> DismissExternalReaderCheck(Guid entryId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        return await shelf.DismissExternalReaderCheckAsync(user.Id, entryId, cancellationToken) ? NoContent() : NotFound();
    }

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

    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv([FromQuery] string? section, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var entries = await shelf.ExportAsync(user.Id, section, cancellationToken);
        return File(exports.CreateCsv(entries), "text/csv; charset=utf-8", ExportFileName(section, "csv"));
    }

    [HttpGet("export/pdf")]
    public async Task<IActionResult> ExportPdf([FromQuery] string? section, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var entries = await shelf.ExportAsync(user.Id, section, cancellationToken);
        return File(exports.CreatePdf(user.Username, entries), "application/pdf", ExportFileName(section, "pdf"));
    }

    private static string ExportFileName(string? section, string extension)
    {
        var suffix = string.IsNullOrWhiteSpace(section) || string.Equals(section, "all", StringComparison.OrdinalIgnoreCase)
            ? "shelf"
            : $"shelf-{section.Trim().ToLowerInvariant()}";
        return $"mangahub-{suffix}.{extension}";
    }
}
