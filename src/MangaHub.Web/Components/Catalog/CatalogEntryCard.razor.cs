using Microsoft.AspNetCore.Components;
using MangaHub.Web.API.DTOs;
using MudBlazor;

namespace MangaHub.Web.Components.Catalog;

public partial class CatalogEntryCard
{
    [Parameter, EditorRequired] public CatalogMangaResponse Entry { get; set; } = default!;
    [Parameter] public string ActiveSourceFilter { get; set; } = "";
    [Parameter] public EventCallback<CatalogMangaResponse> OnEdit { get; set; }
    [Parameter] public EventCallback<string> OnSourceFilter { get; set; }

    private string SourceLabel => FirstNonEmpty(SourceName(Entry.MetadataSource), !string.IsNullOrWhiteSpace(Entry.MyAnimeListId) ? "MAL" : "", !string.IsNullOrWhiteSpace(Entry.OpenLibraryKey) ? "OpenLibrary" : "", "Manual");
    private string ChapterCountLabel => Entry.ChapterCount is null ? "Chapters unknown" : $"{Entry.ChapterCount} chapters";
    private string VolumeCountLabel => Entry.VolumeCount is null ? "Volumes unknown" : $"{Entry.VolumeCount} volumes";
    private bool IsSourceFilterActive => string.Equals(ActiveSourceFilter, SourceLabel, StringComparison.OrdinalIgnoreCase);
    private Variant SourceVariant => IsSourceFilterActive ? Variant.Filled : Variant.Outlined;
    private string SourceScheme => SourceLabel.ToLowerInvariant() switch
    {
        "mal" => "deep",
        "openlibrary" => "warm",
        "manual" => "ink",
        _ => "primary"
    };

    private Color SourceColor => SourceLabel.ToLowerInvariant() switch
    {
        "mal" => Color.Primary,
        "openlibrary" => Color.Secondary,
        "manual" => Color.Default,
        _ => Color.Info
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
