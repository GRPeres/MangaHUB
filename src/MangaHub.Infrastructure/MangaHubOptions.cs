namespace MangaHub.Infrastructure;

public sealed class MangaHubOptions
{
    public string LibraryPath { get; set; } = "/library";
    public bool MangaDexEnabled { get; set; } = true;
    public string JwtSecret { get; set; } = "change-me-before-deploying-to-a-long-random-secret";
    public int JwtExpiresMinutes { get; set; } = 60 * 24 * 7;
    public bool SessionCookieSecure { get; set; }
    public string SessionCookieSameSite { get; set; } = "Lax";
    public string MyAnimeListClientId { get; set; } = "";
    public int MangaDexSyncIntervalHours { get; set; } = 24;
    public int MangaDexSyncDelayMilliseconds { get; set; } = 1500;
    public int MangaDexSyncBatchSize { get; set; } = 50;
    public int MangaDexMaintenanceHour { get; set; } = 4;
    public string MangaDexMaintenanceTimeZone { get; set; } = "America/Sao_Paulo";
    public int MangaDexPrefetchBatchSize { get; set; } = 6;
    public int MangaDexPrefetchMaxChaptersPerManga { get; set; } = 3;
    public int MangaDexPrefetchDelayMilliseconds { get; set; } = 5000;
    public int MangaDexReaderCacheMinutes { get; set; } = 15;
    public int MangaDexReaderMaxChapters { get; set; } = 1000;
    public string MangaDexCachePath { get; set; } = "/mangadex-cache";
}
