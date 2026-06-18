using MangaHub.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Infrastructure.Data;

public sealed class MangaHubDbContext(DbContextOptions<MangaHubDbContext> options) : DbContext(options)
{
    public DbSet<MangaUser> Users => Set<MangaUser>();
    public DbSet<MangaSeries> Series => Set<MangaSeries>();
    public DbSet<MangaChapter> Chapters => Set<MangaChapter>();
    public DbSet<ReadingProgress> ReadingProgress => Set<ReadingProgress>();
    public DbSet<Follow> Follows => Set<Follow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MangaUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.Username).HasMaxLength(80);
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
    }
}

