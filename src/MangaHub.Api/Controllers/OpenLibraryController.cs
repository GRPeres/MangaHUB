using MangaHub.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("api/openlibrary")]
public sealed class OpenLibraryController(OpenLibraryService openLibrary) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken cancellationToken) =>
        Ok(await openLibrary.SearchAsync(q, cancellationToken));
}
