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
    private string FirstPublishYearLabel => Entry.FirstPublishYear?.ToString() ?? "";
    private string ChapterCountLabel => Entry.ChapterCount?.ToString() ?? "";
    private string VolumeCountLabel => Entry.VolumeCount?.ToString() ?? "";
    private string AvailabilityLabel => string.Join(", ", new[]
    {
        !string.IsNullOrWhiteSpace(Entry.MangaDexUrl) ? "MangaDex" : "",
        Entry.LocalSeriesId is not null ? "Local files" : ""
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
    private bool IsSourceFilterActive => string.Equals(ActiveSourceFilter, SourceLabel, StringComparison.OrdinalIgnoreCase);

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
