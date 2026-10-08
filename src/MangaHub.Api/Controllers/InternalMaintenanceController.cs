using System.Security.Cryptography;
using System.Text;
using MangaHub.Api.Services;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("internal/maintenance")]
public sealed class InternalMaintenanceController(
    AdminOperationsService operations,
    MaintenanceWatchdogService watchdog,
    RemoteMaintenanceService remoteMaintenance,
    ILibraryScanner libraryScanner,
    IOptions<MangaHubOptions> options,
    ILogger<InternalMaintenanceController> logger) : ControllerBase
{
    [HttpPost("{type}/queue")]
    public async Task<IActionResult> Queue(string type, [FromQuery] string trigger = "scheduled", CancellationToken cancellationToken = default)
    {
        if (!HasValidWorkerToken())
        {
            return Unauthorized();
        }

        var normalizedTrigger = string.Equals(trigger, "watchdog", StringComparison.OrdinalIgnoreCase) ? "watchdog" : "scheduled";
        var job = await operations.QueueAutomaticAsync(type, normalizedTrigger, cancellationToken);
        return job is null ? BadRequest() : Accepted();
    }

    [HttpPost("watchdog")]
    public async Task<IActionResult> RunWatchdog(CancellationToken cancellationToken)
    {
        if (!HasValidWorkerToken())
        {
            return Unauthorized();
        }

        await watchdog.CheckAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{type}")]
    public async Task<IActionResult> Run(string type, CancellationToken cancellationToken)
    {
        if (!HasValidWorkerToken())
        {
            return Unauthorized();
        }

        try
        {
            if (string.Equals(type, "library-scan", StringComparison.OrdinalIgnoreCase))
            {
                await libraryScanner.ScanAsync(cancellationToken);
                return Ok(new MaintenanceRunResult());
            }

            var result = await remoteMaintenance.RunRequestedAsync(type.Trim().ToLowerInvariant(), cancellationToken);
            return Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Internal maintenance request {Type} failed.", type);
            return Problem(
                title: $"Maintenance task '{type}' failed",
                detail: DescribeFailure(ex),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private bool HasValidWorkerToken()
    {
        var configured = options.Value.InternalWorkerToken;
        var supplied = Request.Headers["X-MangaHub-Worker-Token"].ToString();
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrWhiteSpace(supplied))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(configured),
            Encoding.UTF8.GetBytes(supplied));
    }

    private static string DescribeFailure(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null && messages.Count < 3; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                messages.Add(current.Message.Trim());
            }
        }

        var detail = string.Join(" -> ", messages.Distinct(StringComparer.Ordinal));
        return detail.Length <= 700 ? detail : detail[..700] + "...";
    }
}
