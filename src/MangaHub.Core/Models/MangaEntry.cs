namespace MangaHub.Core.Models;

public sealed class MangaEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CreatedByUserId { get; set; }
    public required string Title { get; set; }
    public string Authors { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public string MetadataSource { get; set; } = "";
    public string MyAnimeListId { get; set; } = "";
    public string OpenLibraryKey { get; set; } = "";
    public int? FirstPublishYear { get; set; }
    public string MediaType { get; set; } = "";
    public string PublishingStatus { get; set; } = "";
    public int? ChapterCount { get; set; }
    public int? VolumeCount { get; set; }
    public string MangaDexUrl { get; set; } = "";
    public string MangaDexId { get; set; } = "";
    public DateTimeOffset? MangaDexLastSyncedAt { get; set; }
    public decimal? MangaDexLastPrefetchedChapter { get; set; }
    public DateTimeOffset? MangaDexLastPrefetchedAt { get; set; }
    public Guid? LocalSeriesId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
