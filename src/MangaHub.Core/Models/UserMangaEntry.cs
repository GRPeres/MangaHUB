namespace MangaHub.Core.Models;

public sealed class UserMangaEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid MangaEntryId { get; set; }
    public string ReadingStatus { get; set; } = "planned";
    public string CurrentChapter { get; set; } = "";
    public int? Score { get; set; }
    public string Category { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public MangaEntry? MangaEntry { get; set; }
}
