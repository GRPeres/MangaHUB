namespace MangaHub.Web.API.DTOs;

public sealed record CreateAdminIssueReportRequest(string Kind, string SubjectType, Guid SubjectId, string Reason, string Note = "");
public sealed record AdminIssueReportStateResponse(Guid IssueId, bool HasMyOpenReport, int ReportCount, string Status);
public sealed record AdminIssueListItemResponse(Guid Id, string Kind, string SubjectType, Guid SubjectId, string Status, string Priority, string Title, string CoverUrl, int ReportCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AdminIssueReportResponse(Guid Id, string Reason, string Note, string SnapshotValue, DateTimeOffset CreatedAt);
public sealed record AdminIssueDetailsResponse(Guid Id, string Kind, string SubjectType, Guid SubjectId, string Status, string Priority, string Title, string CoverUrl, string MetadataJson, string FallbackReaderUrl, string MyAnimeListId, string MangaDexId, string MangaUpdatesId, string ResolutionNote, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, List<AdminIssueReportResponse> Reports);
public sealed record ResolveAdminIssueRequest(string FallbackReaderUrl, string ResolutionNote = "");
public sealed record DismissAdminIssueRequest(string ResolutionNote = "");
public sealed record ReintegrateIssueWithMetadataRequest(
    string MyAnimeListId,
    string Title,
    string Authors,
    string CoverUrl,
    int? FirstPublishYear,
    string Category,
    string Description,
    string MediaType,
    string PublishingStatus,
    int? ChapterCount,
    int? VolumeCount);
public sealed record IssueMetadataReintegrationResponse(bool MetadataAssigned, bool ReaderRestored, string Message, string MangaDexId = "", string MangaDexTitle = "");
