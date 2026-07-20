using MangaHub.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Infrastructure.Data;

public sealed class MangaHubDbContext(DbContextOptions<MangaHubDbContext> options) : DbContext(options)
{
    public DbSet<MangaUser> Users => Set<MangaUser>();
    public DbSet<MangaEntry> MangaEntries => Set<MangaEntry>();
    public DbSet<UserMangaEntry> UserMangaEntries => Set<UserMangaEntry>();
    public DbSet<MangaSeries> Series => Set<MangaSeries>();
    public DbSet<MangaChapter> Chapters => Set<MangaChapter>();
    public DbSet<ReadingProgress> ReadingProgress => Set<ReadingProgress>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<SiteActivity> SiteActivities => Set<SiteActivity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MangaUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.Username).HasMaxLength(80);
            entity.Property(x => x.Role).HasMaxLength(40);
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
            entity.HasIndex(x => x.MangaDexLastSyncedAt);
            entity.Property(x => x.MangaDexLastPrefetchedChapter).HasPrecision(10, 3);
            entity.HasIndex(x => x.MangaDexLastPrefetchedAt);
            entity.HasIndex(x => x.MangaDexLastBackfilledAt);
        });

        modelBuilder.Entity<UserMangaEntry>(entity =>
        {
            entity.ToTable("user_manga_entries");
            entity.HasIndex(x => new { x.UserId, x.MangaEntryId }).IsUnique();
            entity.Property(x => x.ReadingStatus).HasMaxLength(40);
            entity.Property(x => x.CurrentChapter).HasMaxLength(40);
            entity.Property(x => x.Category).HasMaxLength(120);
            entity.HasOne(x => x.MangaEntry).WithMany().HasForeignKey(x => x.MangaEntryId);
        });

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
    }
}
