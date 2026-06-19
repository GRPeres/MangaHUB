namespace MangaHub.Infrastructure;

public sealed class MangaHubOptions
{
    public string LibraryPath { get; set; } = "/library";
    public bool MangaDexEnabled { get; set; } = true;
    public string JwtSecret { get; set; } = "change-me-before-deploying-to-a-long-random-secret";
    public int JwtExpiresMinutes { get; set; } = 60 * 24 * 7;
    public bool SessionCookieSecure { get; set; }
    public string SessionCookieSameSite { get; set; } = "Lax";
}
