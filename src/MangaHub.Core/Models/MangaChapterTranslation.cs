namespace MangaHub.Core.Models;

public sealed class MangaChapterTranslation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MangaChapterId { get; set; }
    public string TargetLanguage { get; set; } = "en";
    public string Status { get; set; } = ChapterTranslationStatus.Pending;
    public string RelativePath { get; set; } = "";
    public int PageCount { get; set; }
    public string FileHash { get; set; } = "";
    public string Error { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public MangaChapter? MangaChapter { get; set; }
}

public static class ChapterTranslationStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Unsupported = "unsupported";
}
