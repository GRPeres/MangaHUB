using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using Microsoft.AspNetCore.Mvc;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController(CurrentUserService currentUsers, AdminService admin, AdminOperationsService operations, MangaHubDbContext db, IHttpClientFactory httpClients) : ControllerBase
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
