using MangaHub.Api.Common;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using MangaHub.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
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
        var passwordError = PasswordRules.Validate(request.Password);
        if (passwordError is not null) return BadRequest(passwordError);
        if (!AuthService.IsValidEmail(request.Email)) return BadRequest("Enter a valid email address.");
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

    [HttpPost("forgot-password")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await auth.RequestPasswordResetAsync(request.Email, cancellationToken);
        return Accepted(new { status = "ok" });
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        return await auth.ResetPasswordAsync(request, cancellationToken)
            ? Ok(new { status = "ok" })
            : BadRequest("This reset link is invalid or has expired.");
    }

    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string token, CancellationToken cancellationToken)
    {
        var verified = await auth.ConfirmEmailAsync(token, cancellationToken);
        return Redirect(verified ? "/account?email=verified" : "/account?email=invalid");
    }

    [HttpPut("account")]
    public async Task<IActionResult> UpdateAccount([FromBody] UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
        if (user is null) return Unauthorized();
        var error = await auth.UpdateAccountAsync(user, request, cancellationToken);
        return error is null ? Ok(ApiMapping.ToUserResponse(user)) : BadRequest(error);
    }

    [HttpGet("google")]
    public async Task<IActionResult> Google([FromQuery] bool link, CancellationToken cancellationToken)
    {
        if (!options.Value.GoogleAuth.IsConfigured) return NotFound();
        var properties = new AuthenticationProperties { RedirectUri = "/auth/google/complete" };
        if (link)
        {
            var user = await currentUsers.GetCurrentUserAsync(Request, cancellationToken);
            if (user is null) return Unauthorized();
            properties.Items["linkUserId"] = user.Id.ToString();
        }
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("google/complete")]
    public async Task<IActionResult> GoogleComplete(CancellationToken cancellationToken)
    {
        var result = await HttpContext.AuthenticateAsync("External");
        if (!result.Succeeded || result.Principal is null) return Redirect("/?login=google-failed");
        var subject = result.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var email = result.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var emailVerified = result.Principal.FindFirst("email_verified")?.Value;
        var name = result.Principal.Identity?.Name ?? email ?? "reader";
        var linkUserId = result.Properties?.Items.TryGetValue("linkUserId", out var linkedUserId) == true
            && Guid.TryParse(linkedUserId, out var parsedUserId)
            ? parsedUserId
            : (Guid?)null;
        await HttpContext.SignOutAsync("External");
        if (string.IsNullOrWhiteSpace(subject) || !AuthService.IsValidEmail(email) || !string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
            return Redirect("/?login=google-failed");
        if (linkUserId is not null)
        {
            var error = await auth.LinkGoogleAsync(linkUserId.Value, subject, cancellationToken);
            return Redirect(error is null ? "/account?google=linked" : "/account?google=failed");
        }
        var user = await auth.LoginWithGoogleAsync(subject, email!, name, cancellationToken);
        sessionCookies.SetSessionCookie(Response, user.SessionToken, options.Value);
        return Redirect("/");
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
