using Microsoft.AspNetCore.Components;
using MangaHub.Web.API.DTOs;

namespace MangaHub.Web.Components.Catalog;

public partial class CatalogEntryCard
{
    [Parameter, EditorRequired] public CatalogMangaResponse Entry { get; set; } = default!;
    [Parameter] public string ActiveSourceFilter { get; set; } = "";
    [Parameter] public EventCallback<CatalogMangaResponse> OnEdit { get; set; }
    [Parameter] public EventCallback<string> OnSourceFilter { get; set; }

    private bool metadataOpen;
    private string SourceLabel => FirstNonEmpty(SourceName(Entry.MetadataSource), !string.IsNullOrWhiteSpace(Entry.MyAnimeListId) ? "MAL" : "", !string.IsNullOrWhiteSpace(Entry.OpenLibraryKey) ? "OpenLibrary" : "", "Manual");
    private List<string> CategoryLabels => SplitLabels(Entry.Category);
    private string VisibleCategoryLabel => CategoryLabels.Count == 0 ? "" : CompactCategoryLabel(CategoryLabels[0]);
    private bool HasHiddenCategories => CategoryLabels.Count > 1;
    private int HiddenCategoryCount => Math.Max(0, CategoryLabels.Count - 1);
    private string ChapterCountLabel => Entry.ChapterCount is null ? "Unknown" : Entry.ChapterCount.Value.ToString();
    private string VolumeCountLabel => Entry.VolumeCount is null ? "Unknown" : Entry.VolumeCount.Value.ToString();
    private string FirstYearLabel => Entry.FirstPublishYear is null ? "Unknown" : Entry.FirstPublishYear.Value.ToString();
    private string FormatLabel => FirstNonEmpty(Entry.MediaType, Entry.Category, "Unknown");
    private string PublishingLabel => FirstNonEmpty(Entry.PublishingStatus, "Unknown");
    private string ShelfAvailabilityLabel => Entry.IsInMyShelf ? "Already on shelf" : "Ready to add";
    private string MangaDexSyncLabel => Entry.MangaDexLastSyncedAt is null
        ? "Not checked yet"
        : Entry.MangaDexLastSyncedAt.Value.ToLocalTime().ToString("g");
    private string CatalogReferenceLabel => (Entry.MyAnimeListId, Entry.OpenLibraryKey) switch
    {
        ({ Length: > 0 }, { Length: > 0 }) => "MAL + OpenLibrary",
        ({ Length: > 0 }, _) => $"MAL #{Entry.MyAnimeListId}",
        (_, { Length: > 0 }) => "OpenLibrary linked",
        _ => "Manual entry"
    };
    private string ReaderLinksLabel => (Entry.MangaDexUrl, Entry.LocalSeriesId) switch
    {
        ({ Length: > 0 }, not null) => "MangaDex + local",
        ({ Length: > 0 }, null) => "MangaDex",
        (_, not null) => "Local files",
        _ => "No reader link"
    };
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
    private string MetadataTitleId => $"catalog-metadata-{Entry.Id:N}";
    private void OpenMetadata() => metadataOpen = true;
    private void CloseMetadata() => metadataOpen = false;

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static List<string> SplitLabels(string value) =>
        (value ?? "").Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string CompactCategoryLabel(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length <= 14 && !trimmed.Any(char.IsWhiteSpace))
        {
            return trimmed;
        }

        var firstWord = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? trimmed;
        return firstWord.Length <= 12 ? $"{firstWord}..." : $"{firstWord[..12]}...";
    }

    private static string SourceName(string source) => source.ToLowerInvariant() switch
    {
        "myanimelist" => "MAL",
        "openlibrary" => "OpenLibrary",
        _ => source
    };
}
