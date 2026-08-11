namespace MangaHub.Core.Models;

public sealed class UsageDailySummary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public int ReaderSeconds { get; set; }
    public int ChaptersCompleted { get; set; }
    public int MangaStarted { get; set; }
    public int MangaCompleted { get; set; }
    public int ShelfChanges { get; set; }
    public int CatalogChanges { get; set; }
    public int Searches { get; set; }
    public int NotificationOpens { get; set; }
    public int SignIns { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
