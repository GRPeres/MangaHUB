namespace MangaHub.Core.Models;

public sealed class IssueProposalSchedule
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    public bool WebResearchEnabled { get; set; } = true;
    public int BatchIntervalMinutes { get; set; } = 1440;
    public int RequestIntervalSeconds { get; set; } = 30;
    public int GroupSize { get; set; } = 5;
    public DateTimeOffset? NextRunAt { get; set; }
    public Guid? RunId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public string LastError { get; set; } = "";

    public bool CanStart(DateTimeOffset now, bool hasUnreadDrafts) =>
        Enabled && RunId is null && (NextRunAt is null || NextRunAt <= now) && !hasUnreadDrafts;
}

public sealed class ScheduledIssueProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid GroupId { get; set; }
    public string EvidenceKey { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTimeOffset? ReviewedAt { get; set; }
}
