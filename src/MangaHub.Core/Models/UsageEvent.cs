namespace MangaHub.Core.Models;

public sealed class UsageEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string EventType { get; set; } = "";
    public Guid? MangaEntryId { get; set; }
    public Guid? ChapterId { get; set; }
    public string SessionId { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public int? DurationSeconds { get; set; }
    public string MetadataJson { get; set; } = "{}";
}
