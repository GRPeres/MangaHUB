namespace MangaHub.Core.Models;

/// <summary>Server-side cache recovery telemetry. This is operational data, not user analytics.</summary>
public sealed class ArchiveRecoveryEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MangaDexId { get; set; } = "";
    public string ChapterSourceId { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
