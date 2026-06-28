using Microsoft.AspNetCore.Components;
using MangaHub.Web.API.DTOs;

namespace MangaHub.Web.Components.Catalog;

public partial class CatalogEntryCard
{
    [Parameter, EditorRequired] public CatalogMangaResponse Entry { get; set; } = default!;
    [Parameter] public string ActiveSourceFilter { get; set; } = "";
    [Parameter] public EventCallback<CatalogMangaResponse> OnEdit { get; set; }
    [Parameter] public EventCallback<string> OnSourceFilter { get; set; }

    private string SourceLabel => FirstNonEmpty(SourceName(Entry.MetadataSource), !string.IsNullOrWhiteSpace(Entry.MyAnimeListId) ? "MAL" : "", !string.IsNullOrWhiteSpace(Entry.OpenLibraryKey) ? "OpenLibrary" : "", "Manual");
    private string ChapterCountLabel => Entry.ChapterCount is null ? "Unknown" : Entry.ChapterCount.Value.ToString();
    private string VolumeCountLabel => Entry.VolumeCount is null ? "Unknown" : Entry.VolumeCount.Value.ToString();
    private string FirstYearLabel => Entry.FirstPublishYear is null ? "Unknown" : Entry.FirstPublishYear.Value.ToString();
    private string FormatLabel => FirstNonEmpty(Entry.MediaType, Entry.Category, "Unknown");
    private string PublishingLabel => FirstNonEmpty(Entry.PublishingStatus, "Unknown");
    private bool IsSourceFilterActive => string.Equals(ActiveSourceFilter, SourceLabel, StringComparison.OrdinalIgnoreCase);
    private string SourceFilterHint => IsSourceFilterActive ? "Filtered" : "Click to filter";
    private string SourceScheme => SourceLabel.ToLowerInvariant() switch
    {
        "mal" => "deep",
        "openlibrary" => "warm",
        "manual" => "ink",
        _ => "primary"
    };

    private Task Edit() => OnEdit.InvokeAsync(Entry);
    private Task FilterBySource() => OnSourceFilter.InvokeAsync(SourceLabel);

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string SourceName(string source) => source.ToLowerInvariant() switch
    {
        "myanimelist" => "MAL",
        "openlibrary" => "OpenLibrary",
        _ => source
    };
}
