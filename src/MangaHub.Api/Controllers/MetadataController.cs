using MangaHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/metadata")]
public sealed class MetadataController(MetadataService metadata) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] bool includeOpenLibrary, CancellationToken cancellationToken) =>
        Ok(await metadata.SearchAsync(q, includeOpenLibrary, cancellationToken));
}
