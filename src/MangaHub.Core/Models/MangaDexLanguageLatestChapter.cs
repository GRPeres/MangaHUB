namespace MangaHub.Core.Models;

public sealed class MangaDexLanguageLatestChapter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MangaEntryId { get; set; }
    public string Language { get; set; } = "en";
    public decimal LatestChapter { get; set; }
    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
}
