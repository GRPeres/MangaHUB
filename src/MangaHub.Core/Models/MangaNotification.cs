namespace MangaHub.Core.Models;

public sealed class MangaNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid MangaEntryId { get; set; }
    public string Type { get; set; } = "new-chapter";
    public decimal ChapterNumber { get; set; }
    public string Language { get; set; } = "en";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}
