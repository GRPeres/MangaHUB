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
    public int MangaDexSyncIntervalHours { get; set; } = 6;
    public int MangaDexReleasePollMinutes { get; set; } = 30;
    public int MangaDexSyncBatchSize { get; set; } = 50;
    public int MangaDexSyncMaxBatchSize { get; set; } = 1000;
    public int MangaDexMaintenanceHour { get; set; } = 4;
    public string MangaDexMaintenanceTimeZone { get; set; } = "America/Sao_Paulo";
    public int MangaDexPrefetchBatchSize { get; set; } = 6;
    public int MangaDexPrefetchMaxChaptersPerManga { get; set; } = 3;
    public bool MangaDexIdleBackfillEnabled { get; set; } = true;
    public int MangaDexIdleMinutes { get; set; } = 30;
    public int MangaDexIdleBackfillCheckMinutes { get; set; } = 60;
    public int MangaDexIdleBackfillBatchSize { get; set; } = 1;
    public int MangaDexIdleBackfillMaxChaptersPerManga { get; set; } = 2;
    public int MangaDexReaderCacheMinutes { get; set; } = 15;
    public int MangaDexReaderMaxChapters { get; set; } = 1000;
    public string MangaDexCachePath { get; set; } = "/mangadex-cache";
    public ChapterTranslationOptions Translation { get; set; } = new();
    public bool MangaUpdatesEnabled { get; set; } = true;
    public int MangaUpdatesReleasePollMinutes { get; set; } = 60;
    public int MangaUpdatesSyncIntervalHours { get; set; } = 12;
    public int MangaUpdatesMatchPollMinutes { get; set; } = 15;
    public int MangaUpdatesMatchRetryHours { get; set; } = 24;
    public int MangaUpdatesSyncBatchSize { get; set; } = 25;
    public int MangaUpdatesMatchBatchSize { get; set; } = 10;
    public RemoteJobs.RemoteRequestLimitsOptions RemoteRequests { get; set; } = new();
}

public sealed class ChapterTranslationOptions
{
    public bool Enabled { get; set; }
    public string LibreTranslateUrl { get; set; } = "http://libretranslate:5000";
    public string LibreTranslateApiKey { get; set; } = "";
    public string TesseractCommand { get; set; } = "tesseract";
    public string FontFamily { get; set; } = "Noto Sans";
    public int MinimumOcrConfidence { get; set; } = 35;
    public int RequestTimeoutSeconds { get; set; } = 300;
}
