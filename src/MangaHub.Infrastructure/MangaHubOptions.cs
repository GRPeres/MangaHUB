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
    public bool MangaDexCacheRetentionEnabled { get; set; } = true;
    public int MangaDexReaderCacheMinutes { get; set; } = 15;
    public int MangaDexReaderMaxChapters { get; set; } = 1000;
    public int ExternalReaderCheckIntervalDays { get; set; } = 7;
    public string MangaDexCachePath { get; set; } = "/mangadex-cache";
    public bool MangaUpdatesEnabled { get; set; } = true;
    public int MangaUpdatesReleasePollMinutes { get; set; } = 60;
    public int MangaUpdatesSyncIntervalHours { get; set; } = 12;
    public int MangaUpdatesMatchPollMinutes { get; set; } = 15;
    public int MangaUpdatesMatchRetryHours { get; set; } = 24;
    public int MangaUpdatesSyncBatchSize { get; set; } = 25;
    public int MangaUpdatesMatchBatchSize { get; set; } = 10;
    public RemoteJobs.RemoteRequestLimitsOptions RemoteRequests { get; set; } = new();
    public WebPushOptions WebPush { get; set; } = new();
    public EmailOptions Email { get; set; } = new();
    public GoogleAuthOptions GoogleAuth { get; set; } = new();
}

public sealed class WebPushOptions { public string PublicKey { get; set; } = ""; public string PrivateKey { get; set; } = ""; public string Subject { get; set; } = "mailto:admin@mangahub.app"; }
public sealed class EmailOptions
{
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string SmtpUsername { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "MangaHub";
}
public sealed class GoogleAuthOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
