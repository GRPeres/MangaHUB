namespace MangaHub.Core.Models;

public sealed class MangaEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string Title { get; set; }
    public string Authors { get; set; } = "";
    public string Description { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public string OpenLibraryKey { get; set; } = "";
    public int? FirstPublishYear { get; set; }
    public string ReadingStatus { get; set; } = "planned";
    public string MangaDexUrl { get; set; } = "";
    public string MangaDexId { get; set; } = "";
    public Guid? LocalSeriesId { get; set; }
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

