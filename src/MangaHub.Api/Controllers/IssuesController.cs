using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/issues")]
public sealed class IssuesController(CurrentUserService currentUsers, IssueReportingService issues) : ControllerBase
{
    [HttpPost("reports")]
    public async Task<IActionResult> Report([FromBody] CreateAdminIssueReportRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        var result = await issues.ReportAsync(user.Id, request, cancellationToken);
        return result is null ? BadRequest("This issue report is not available for the selected manga.") : Ok(result);
    }

    [HttpGet("report-state")]
    public async Task<IActionResult> ReportState([FromQuery] string kind, [FromQuery] string subjectType, [FromQuery] Guid subjectId, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        var result = await issues.GetMyReportStateAsync(user.Id, kind, subjectType, subjectId, cancellationToken);
        return result is null ? BadRequest() : Ok(result);
    }
}
