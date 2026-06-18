namespace MangaHub.Core.Models;

public sealed class ReadingProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SeriesId { get; set; }
    public Guid ChapterId { get; set; }
    public int Page { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

