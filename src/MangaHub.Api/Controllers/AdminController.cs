using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using Microsoft.AspNetCore.Mvc;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController(CurrentUserService currentUsers, AdminService admin, AdminOperationsService operations, IssueReportingService issues, MangaHubDbContext db, IHttpClientFactory httpClients) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken)
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

        return Ok(await admin.ListUsersAsync(cancellationToken));
    }

    [HttpGet("diagnostics/database")]
    public async Task<IActionResult> TestDatabase(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var connected = await db.Database.CanConnectAsync(cancellationToken);
        return Ok(new DiagnosticResult(connected, connected ? "PostgreSQL is reachable." : "PostgreSQL could not be reached."));
    }

    [HttpGet("diagnostics/mangadex")]
    public async Task<IActionResult> TestMangaDex(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        try
        {
            using var response = await httpClients.CreateClient("mangadex-sync").GetAsync("/ping", cancellationToken);
            return Ok(new DiagnosticResult(response.IsSuccessStatusCode, response.IsSuccessStatusCode ? "MangaDex API is reachable." : $"MangaDex returned HTTP {(int)response.StatusCode}."));
        }
        catch (HttpRequestException ex)
        {
            return Ok(new DiagnosticResult(false, $"MangaDex connection failed: {ex.Message}"));
        }
    }

    [HttpGet("operations")]
    public async Task<IActionResult> Operations(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(await operations.GetOverviewAsync(cancellationToken));
    }

    [HttpGet("issues")]
    public async Task<IActionResult> Issues([FromQuery] string? status, [FromQuery] int offset = 0, [FromQuery] int limit = 40, CancellationToken cancellationToken = default)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(await issues.ListAsync(status, offset, limit, cancellationToken));
    }

    [HttpGet("issues/open-count")]
    public async Task<IActionResult> OpenIssueCount(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return Ok(await issues.CountOpenAsync(cancellationToken));
    }

    [HttpGet("issues/{issueId:guid}")]
    public async Task<IActionResult> Issue(Guid issueId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var result = await issues.GetDetailsAsync(issueId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("issues/{issueId:guid}/resolve")]
    public async Task<IActionResult> ResolveIssue(Guid issueId, [FromBody] ResolveAdminIssueRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return await issues.ResolveExternalReaderLinkAsync(user.Id, issueId, request, cancellationToken) ? NoContent() : BadRequest("Enter a valid replacement URL.");
    }

    [HttpPost("issues/{issueId:guid}/reintegrate-mangadex")]
    public async Task<IActionResult> ReintegrateWithMangaDex(Guid issueId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var match = await issues.ReintegrateWithMangaDexAsync(user.Id, issueId, cancellationToken);
        return match is null
            ? NotFound("No automatic MangaDex match was found. Keep or replace the external link instead.")
            : Ok(match);
    }

    [HttpPost("issues/{issueId:guid}/reintegrate-metadata")]
    public async Task<IActionResult> ReintegrateWithSelectedMetadata(Guid issueId, [FromBody] ReintegrateIssueWithMetadataRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var result = await issues.ReintegrateWithSelectedMetadataAsync(user.Id, issueId, request, cancellationToken);
        return result is null ? BadRequest("Select a valid MyAnimeList result for this open external reader issue.") : Ok(result);
    }

    [HttpPost("issues/{issueId:guid}/merge-duplicates")]
    public async Task<IActionResult> MergeDuplicateCatalogIssue(Guid issueId, [FromBody] MergeDuplicateCatalogIssueRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return await issues.MergeDuplicateCatalogIssueAsync(user.Id, issueId, request, cancellationToken)
            ? NoContent()
            : BadRequest("Choose a valid duplicate catalog entry to keep.");
    }

    [HttpPost("issues/{issueId:guid}/dismiss")]
    public async Task<IActionResult> DismissIssue(Guid issueId, [FromBody] DismissAdminIssueRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return await issues.DismissAsync(user.Id, issueId, request, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("issues/{issueId:guid}/reopen")]
    public async Task<IActionResult> ReopenIssue(Guid issueId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        return await issues.ReopenAsync(user.Id, issueId, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("operations/jobs")]
    public async Task<IActionResult> QueueJob([FromBody] QueueMaintenanceJobRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        if (!CurrentUserService.IsAdmin(user)) return StatusCode(StatusCodes.Status403Forbidden);
        var job = await operations.QueueAsync(user.Id, request.Type, cancellationToken);
        return job is null ? BadRequest("Unsupported maintenance job.") : Accepted(job);
    }

    [HttpPut("users/{userId:guid}/role")]
    public async Task<IActionResult> UpdateRole(Guid userId, [FromBody] UpdateUserRoleRequest request, CancellationToken cancellationToken)
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

        var result = await admin.UpdateRoleAsync(userId, request, cancellationToken);
        return result.Error switch
        {
            null => Ok(result.User),
            "not_found" => NotFound(),
            "bad_role" => BadRequest("Role must be admin or user."),
            "last_admin" => BadRequest("At least one admin must remain."),
            _ => BadRequest()
        };
    }
}
