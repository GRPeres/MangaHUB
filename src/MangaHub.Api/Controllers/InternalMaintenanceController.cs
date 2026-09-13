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
    RemoteMaintenanceService remoteMaintenance,
    ILibraryScanner libraryScanner,
    IOptions<MangaHubOptions> options) : ControllerBase
{
    [HttpPost("{type}")]
    public async Task<IActionResult> Run(string type, CancellationToken cancellationToken)
    {
        if (!HasValidWorkerToken())
        {
            return Unauthorized();
        }

        if (string.Equals(type, "library-scan", StringComparison.OrdinalIgnoreCase))
        {
            await libraryScanner.ScanAsync(cancellationToken);
            return NoContent();
        }

        await remoteMaintenance.RunRequestedAsync(type.Trim().ToLowerInvariant(), cancellationToken);
        return NoContent();
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
}
