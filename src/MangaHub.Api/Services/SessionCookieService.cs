using MangaHub.Infrastructure;

namespace MangaHub.Api.Services;

public sealed class SessionCookieService
{
    public void SetSessionCookie(HttpResponse response, string token, MangaHubOptions options)
    {
        var cookieOptions = BuildSessionCookieOptions(options);
        cookieOptions.MaxAge = TimeSpan.FromDays(7);
        response.Cookies.Append("mangahub_session", token, cookieOptions);
    }

    public void ClearSessionCookie(HttpResponse response, MangaHubOptions options)
    {
        response.Cookies.Delete("mangahub_session", BuildSessionCookieOptions(options));
    }

    private static CookieOptions BuildSessionCookieOptions(MangaHubOptions options) =>
        new()
        {
            HttpOnly = true,
            Path = "/",
            SameSite = ParseSameSiteMode(options.SessionCookieSameSite),
            Secure = options.SessionCookieSecure
        };

    private static SameSiteMode ParseSameSiteMode(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "none" => SameSiteMode.None,
            "strict" => SameSiteMode.Strict,
            _ => SameSiteMode.Lax
        };
}
