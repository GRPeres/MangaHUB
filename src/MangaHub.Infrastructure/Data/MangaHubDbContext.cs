using MangaHub.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Infrastructure.Data;

public sealed class MangaHubDbContext(DbContextOptions<MangaHubDbContext> options) : DbContext(options)
{
    public DbSet<MangaUser> Users => Set<MangaUser>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<MangaEntry> MangaEntries => Set<MangaEntry>();
    public DbSet<MangaDexLanguageLatestChapter> MangaDexLanguageLatestChapters => Set<MangaDexLanguageLatestChapter>();
    public DbSet<MangaNotification> Notifications => Set<MangaNotification>();
    public DbSet<WebPushSubscription> WebPushSubscriptions => Set<WebPushSubscription>();
    public DbSet<UserMangaEntry> UserMangaEntries => Set<UserMangaEntry>();
    public DbSet<MangaSeries> Series => Set<MangaSeries>();
    public DbSet<MangaChapter> Chapters => Set<MangaChapter>();
    public DbSet<ReadingProgress> ReadingProgress => Set<ReadingProgress>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<SiteActivity> SiteActivities => Set<SiteActivity>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<UsageDailySummary> UsageDailySummaries => Set<UsageDailySummary>();
    public DbSet<MaintenanceJob> MaintenanceJobs => Set<MaintenanceJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MangaUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.Username).HasMaxLength(80);
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.Property(x => x.PendingEmail).HasMaxLength(320);
            entity.Property(x => x.GoogleSubject).HasMaxLength(255);
            entity.Property(x => x.Role).HasMaxLength(40);
            entity.Property(x => x.PreferredLanguage).HasMaxLength(128);
            entity.HasIndex(x => x.Email).IsUnique().HasFilter("\"Email\" <> ''");
            entity.HasIndex(x => x.GoogleSubject).IsUnique().HasFilter("\"GoogleSubject\" <> ''");
        });

        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.ToTable("email_verification_tokens");
            entity.Property(x => x.TokenHash).HasMaxLength(128);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.ToTable("password_reset_tokens");
            entity.Property(x => x.TokenHash).HasMaxLength(128);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
        });

        modelBuilder.Entity<MangaEntry>(entity =>
        {
            entity.ToTable("manga_entries");
            entity.HasIndex(x => x.OpenLibraryKey);
            entity.HasIndex(x => x.MyAnimeListId);
            entity.Property(x => x.Title).HasMaxLength(255);
            entity.Property(x => x.Category).HasMaxLength(120);
            entity.Property(x => x.MetadataSource).HasMaxLength(40);
            entity.Property(x => x.MyAnimeListId).HasMaxLength(80);
            entity.Property(x => x.MediaType).HasMaxLength(80);
            entity.Property(x => x.PublishingStatus).HasMaxLength(80);
            entity.Property(x => x.MangaDexId).HasMaxLength(80);
            entity.Property(x => x.FallbackReaderUrl).HasColumnType("text");
            entity.Property(x => x.ReaderPreference).HasMaxLength(20);
            entity.Property(x => x.MangaDexLatestChapter).HasPrecision(10, 3);
            entity.HasIndex(x => x.MangaDexLastSyncedAt);
            entity.Property(x => x.MangaDexLastPrefetchedChapter).HasPrecision(10, 3);
            entity.HasIndex(x => x.MangaDexLastPrefetchedAt);
            entity.HasIndex(x => x.MangaDexLastBackfilledAt);
            entity.Property(x => x.MangaUpdatesId).HasMaxLength(32);
            entity.HasIndex(x => x.MangaUpdatesId);
            entity.Property(x => x.MangaUpdatesLatestChapter).HasPrecision(10, 3);
            entity.HasIndex(x => x.MangaUpdatesLastSyncedAt);
            entity.HasIndex(x => x.MangaUpdatesLastMatchAttemptAt);
        });

        modelBuilder.Entity<UserMangaEntry>(entity =>
        {
            entity.ToTable("user_manga_entries");
            entity.HasIndex(x => new { x.UserId, x.MangaEntryId }).IsUnique();
            entity.Property(x => x.ReadingStatus).HasMaxLength(40);
            entity.Property(x => x.CurrentChapter).HasMaxLength(40);
            entity.Property(x => x.Category).HasMaxLength(120);
            entity.HasIndex(x => new { x.UserId, x.LastExternalReaderVerifiedAt });
            entity.HasIndex(x => new { x.UserId, x.ExternalReaderCheckPendingAt });
            entity.HasOne(x => x.MangaEntry).WithMany().HasForeignKey(x => x.MangaEntryId);
        });

        modelBuilder.Entity<MangaDexLanguageLatestChapter>(entity =>
        {
            entity.ToTable("mangadex_language_latest_chapters");
            entity.HasIndex(x => new { x.MangaEntryId, x.Language }).IsUnique();
            entity.Property(x => x.Language).HasMaxLength(16);
            entity.Property(x => x.LatestChapter).HasPrecision(10, 3);
        });

        modelBuilder.Entity<MangaNotification>(entity =>
        {
            entity.ToTable("notifications");
            entity.Property(x => x.Type).HasMaxLength(40);
            entity.Property(x => x.Language).HasMaxLength(16);
            entity.Property(x => x.ChapterNumber).HasPrecision(10, 3);
            entity.Property(x => x.Title).HasMaxLength(255);
            entity.Property(x => x.Body).HasColumnType("text");
            entity.HasIndex(x => new { x.UserId, x.MangaEntryId, x.Type, x.ChapterNumber, x.Language }).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
        });
        modelBuilder.Entity<WebPushSubscription>(entity => { entity.ToTable("web_push_subscriptions"); entity.HasIndex(x => x.Endpoint).IsUnique(); entity.Property(x => x.Endpoint).HasColumnType("text"); entity.Property(x => x.P256dh).HasColumnType("text"); entity.Property(x => x.Auth).HasColumnType("text"); entity.Property(x => x.DeviceLabel).HasMaxLength(120); });

        modelBuilder.Entity<MangaSeries>(entity =>
        {
            entity.ToTable("series");
            entity.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
            entity.Property(x => x.Title).HasMaxLength(255);
            entity.Property(x => x.Source).HasMaxLength(40);
        });

        modelBuilder.Entity<MangaChapter>(entity =>
        {
            entity.ToTable("chapters");
            entity.HasIndex(x => new { x.SeriesId, x.SourceId }).IsUnique();
            entity.Property(x => x.Language).HasMaxLength(16);
            entity.HasOne(x => x.Series).WithMany(x => x.Chapters).HasForeignKey(x => x.SeriesId);
        });

        modelBuilder.Entity<ReadingProgress>(entity =>
        {
            entity.ToTable("reading_progress");
            entity.HasIndex(x => new { x.UserId, x.SeriesId }).IsUnique();
        });

        modelBuilder.Entity<Follow>(entity =>
        {
            entity.ToTable("follows");
            entity.HasIndex(x => new { x.UserId, x.SeriesId }).IsUnique();
        });

        modelBuilder.Entity<SiteActivity>(entity =>
        {
            entity.ToTable("site_activity");
            entity.HasKey(x => x.Id);
        });

        modelBuilder.Entity<UsageEvent>(entity =>
        {
            entity.ToTable("usage_events");
            entity.Property(x => x.EventType).HasMaxLength(80);
            entity.Property(x => x.SessionId).HasMaxLength(80);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(160);
            entity.Property(x => x.MetadataJson).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.UserId, x.OccurredAt });
            entity.HasIndex(x => new { x.UserId, x.EventType, x.OccurredAt });
            entity.HasIndex(x => x.MangaEntryId);
            entity.HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique().HasFilter("\"IdempotencyKey\" <> ''");
        });

        modelBuilder.Entity<UsageDailySummary>(entity =>
        {
            entity.ToTable("usage_daily_summaries");
            entity.HasIndex(x => new { x.UserId, x.Date }).IsUnique();
        });

        modelBuilder.Entity<MaintenanceJob>(entity =>
        {
            entity.ToTable("maintenance_jobs");
            entity.Property(x => x.Type).HasMaxLength(80);
            entity.Property(x => x.Status).HasMaxLength(20);
            entity.Property(x => x.Error).HasColumnType("text");
            entity.HasIndex(x => new { x.Status, x.RequestedAt });
        });
    }
}
