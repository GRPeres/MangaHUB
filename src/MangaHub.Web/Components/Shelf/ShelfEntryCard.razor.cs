using Microsoft.AspNetCore.Components;
using MangaHub.Web.API.DTOs;
using MudBlazor;

namespace MangaHub.Web.Components.Shelf;

public partial class ShelfEntryCard
{
    [Parameter, EditorRequired] public MangaEntryResponse Entry { get; set; } = default!;
    [Parameter] public string ActiveStatusFilter { get; set; } = "";
    [Parameter] public EventCallback<MangaEntryResponse> OnEdit { get; set; }
    [Parameter] public EventCallback<Guid> OnRead { get; set; }
    [Parameter] public EventCallback<string> OnStatusFilter { get; set; }

    private string StatusLabel => string.IsNullOrWhiteSpace(Entry.ReadingStatus) ? "planned" : Entry.ReadingStatus;
    private string GenreLabel => FirstNonEmpty(Entry.Category, Entry.CatalogCategory);
    private string SourceLabel => FirstNonEmpty(SourceName(Entry.MetadataSource), !string.IsNullOrWhiteSpace(Entry.MyAnimeListId) ? "MAL" : "", !string.IsNullOrWhiteSpace(Entry.OpenLibraryKey) ? "OpenLibrary" : "");
    private string SummaryText => FirstNonEmpty(Entry.Summary, Entry.Description);
    private string CurrentChapterLabel => string.IsNullOrWhiteSpace(Entry.CurrentChapter) ? "Not started" : $"Ch. {Entry.CurrentChapter}";
    private string LatestChapterLabel => Entry.ChapterCount is null ? "Unknown" : $"Ch. {Entry.ChapterCount}";
    private string AvailabilityLabel => string.Join(", ", new[]
    {
        !string.IsNullOrWhiteSpace(Entry.MangaDexUrl) ? "MangaDex" : "",
        Entry.LocalSeriesId is not null ? "Local files" : ""
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
    private bool IsStatusFilterActive => string.Equals(ActiveStatusFilter, StatusLabel, StringComparison.OrdinalIgnoreCase);
    private string StatusTone => StatusLabel.ToLowerInvariant() switch
    {
        "reading" => "progress",
        "done" => "success",
        "paused" => "warning",
        "planned" => "source",
        "dropped" => "error",
        _ => "neutral"
    };
    private string StatusIcon => StatusLabel.ToLowerInvariant() switch
    {
        "reading" => Icons.Material.Filled.AutoStories,
        "done" => Icons.Material.Filled.CheckCircle,
        "paused" => Icons.Material.Filled.PauseCircle,
        "planned" => Icons.Material.Filled.EventNote,
        "dropped" => Icons.Material.Filled.Cancel,
        _ => Icons.Material.Filled.Bookmark
    };

    private Task Edit() => OnEdit.InvokeAsync(Entry);
    private Task Read() => OnRead.InvokeAsync(Entry.Id);
    private Task FilterByStatus() => OnStatusFilter.InvokeAsync(StatusLabel);

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string SourceName(string source) => source.ToLowerInvariant() switch
    {
        "myanimelist" => "MAL",
        "openlibrary" => "OpenLibrary",
        _ => source
    };
}
