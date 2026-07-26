using MangaHub.Api.Common;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using MangaHub.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(
    AuthService auth,
    CurrentUserService currentUsers,
    SessionCookieService sessionCookies,
    IOptions<MangaHubOptions> options) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] AuthRequest request, CancellationToken cancellationToken)
    {
        var user = await auth.RegisterAsync(request, cancellationToken);
        if (user is null)
        {
            return Conflict("Username already exists.");
        }

        sessionCookies.SetSessionCookie(Response, user.SessionToken, options.Value);
        return Created("/auth/me", user);
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] AuthRequest request, CancellationToken cancellationToken)
    {
        var user = await auth.LoginAsync(request, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        sessionCookies.SetSessionCookie(Response, user.SessionToken, options.Value);
        return Ok(user);
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        sessionCookies.ClearSessionCookie(Response, options.Value);
        return Ok(new { status = "ok" });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null
            ? Unauthorized()
            : Ok(ApiMapping.ToUserResponse(user));
    }

    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferredLanguageRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        return user is null
            ? Unauthorized()
            : Ok(await auth.UpdatePreferredLanguageAsync(user, request, cancellationToken));
    }
}
