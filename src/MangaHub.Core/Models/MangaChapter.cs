namespace MangaHub.Core.Models;

public sealed class MangaChapter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeriesId { get; set; }
    public string ChapterNumber { get; set; } = "";
    public string Language { get; set; } = "en";
    public string Title { get; set; } = "";
    public required string SourceId { get; set; }
    public int PageCount { get; set; }
    public string FileHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public MangaSeries? Series { get; set; }
}
