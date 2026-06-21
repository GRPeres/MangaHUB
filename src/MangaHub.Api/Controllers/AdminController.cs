using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController(CurrentUserService currentUsers, AdminService admin) : ControllerBase
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
