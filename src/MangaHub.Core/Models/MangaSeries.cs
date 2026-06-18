namespace MangaHub.Core.Models;

public sealed class MangaSeries
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string Description { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public string Status { get; set; } = "unknown";
    public required string Source { get; set; }
    public required string ExternalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<MangaChapter> Chapters { get; set; } = [];
}

