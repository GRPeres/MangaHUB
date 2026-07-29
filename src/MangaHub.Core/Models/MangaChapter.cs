namespace MangaHub.Core.Models;

public sealed class MangaChapter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SeriesId { get; set; }
    public string ChapterNumber { get; set; } = "";
    // Legacy display/source field retained while existing cache records are migrated.
    public string Language { get; set; } = "en";
    public string SourceLanguage { get; set; } = "en";
    public bool IsCanonical { get; set; } = true;
    public string Title { get; set; } = "";
    public required string SourceId { get; set; }
    public int PageCount { get; set; }
    public string FileHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public MangaSeries? Series { get; set; }
    public List<MangaChapterTranslation> Translations { get; set; } = [];
}
