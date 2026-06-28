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
    private string SourceOrManualLabel => FirstNonEmpty(SourceLabel, "Manual");
    private string FormatLabel => FirstNonEmpty(Entry.MediaType, GenreLabel, "Unknown");
    private string ScoreLabel => Entry.Score is null ? "Not scored" : $"{Entry.Score}/5";
    private string ReadSourceLabel => !string.IsNullOrWhiteSpace(Entry.MangaDexUrl)
        ? "MangaDex"
        : Entry.LocalSeriesId is not null ? "Local" : "Unlinked";
    private string CurrentChapterLabel => string.IsNullOrWhiteSpace(Entry.CurrentChapter) ? "Current: not started" : $"Current: ch. {Entry.CurrentChapter}";
    private string LatestChapterLabel => Entry.ChapterCount is null ? "Latest: unknown" : $"Latest: ch. {Entry.ChapterCount}";
    private bool IsStatusFilterActive => string.Equals(ActiveStatusFilter, StatusLabel, StringComparison.OrdinalIgnoreCase);
    private string StatusFilterHint => IsStatusFilterActive ? "Filtered" : "Click to filter";
    private Variant StatusVariant => IsStatusFilterActive ? Variant.Filled : Variant.Outlined;
    private string StatusIcon => StatusLabel.ToLowerInvariant() switch
    {
        "reading" => Icons.Material.Filled.AutoStories,
        "done" => Icons.Material.Filled.TaskAlt,
        "paused" => Icons.Material.Filled.PauseCircle,
        "planned" => Icons.Material.Filled.BookmarkAdd,
        "dropped" => Icons.Material.Filled.RemoveCircle,
        _ => Icons.Material.Filled.LocalLibrary
    };

    private string StatusScheme => StatusLabel.ToLowerInvariant() switch
    {
        "reading" => "deep",
        "done" => "soft",
        "paused" => "warm",
        "planned" => "secondary",
        "dropped" => "ink",
        _ => "primary"
    };

    private Color StatusColor => StatusLabel.ToLowerInvariant() switch
    {
        "reading" => Color.Info,
        "done" => Color.Success,
        "paused" => Color.Warning,
        "planned" => Color.Secondary,
        "dropped" => Color.Error,
        _ => Color.Primary
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
