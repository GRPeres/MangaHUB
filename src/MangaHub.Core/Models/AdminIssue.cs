namespace MangaHub.Core.Models;

public sealed class AdminIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "";
    public string SubjectType { get; set; } = "";
    public Guid SubjectId { get; set; }
    public string Status { get; set; } = "open";
    public string Priority { get; set; } = "normal";
    public string TitleSnapshot { get; set; } = "";
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string ResolutionNote { get; set; } = "";
    public List<AdminIssueReport> Reports { get; set; } = [];
}

public sealed class AdminIssueReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AdminIssueId { get; set; }
    public Guid ReporterUserId { get; set; }
    public string Reason { get; set; } = "other";
    public string Note { get; set; } = "";
    public string SnapshotValue { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public AdminIssue? Issue { get; set; }
}
