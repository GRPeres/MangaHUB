using MangaHub.Api.Common;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/usage")]
public sealed class UsageController(CurrentUserService currentUsers, UsageTrackingService usage) : ControllerBase
{
    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateUsageAnalyticsRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        await usage.SetEnabledAsync(user, request.Enabled, cancellationToken);
        return Ok(ApiMapping.ToUserResponse(user));
    }

    [HttpPost("events")]
    public async Task<IActionResult> Track([FromBody] UsageTelemetryRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        await usage.TrackClientAsync(user.Id, request, cancellationToken);
        return NoContent();
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null ? Unauthorized() : Ok(await usage.GetDashboardAsync(user.Id, days, cancellationToken));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        return File(System.Text.Encoding.UTF8.GetBytes(await usage.ExportAsync(user.Id, cancellationToken)), "application/json", "mangahub-analytics.json");
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        await usage.DeleteAsync(user.Id, cancellationToken);
        return NoContent();
    }
}
