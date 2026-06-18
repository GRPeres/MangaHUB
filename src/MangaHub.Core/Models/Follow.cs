namespace MangaHub.Core.Models;

public sealed class Follow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SeriesId { get; set; }
}

