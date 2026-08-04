using Microsoft.AspNetCore.Components;
using MangaHub.Web.API.DTOs;

namespace MangaHub.Web.Components.Catalog;

public partial class CatalogEntryCard
{
    [Parameter, EditorRequired] public CatalogMangaResponse Entry { get; set; } = default!;
    [Parameter] public string ActiveSourceFilter { get; set; } = "";
    [Parameter] public EventCallback<CatalogMangaResponse> OnEdit { get; set; }
    [Parameter] public EventCallback<CatalogMangaResponse> OnManageCache { get; set; }
    [Parameter] public EventCallback<string> OnSourceFilter { get; set; }

    private bool metadataOpen;
    private string SourceLabel => FirstNonEmpty(SourceName(Entry.MetadataSource), !string.IsNullOrWhiteSpace(Entry.MyAnimeListId) ? "MAL" : "", !string.IsNullOrWhiteSpace(Entry.OpenLibraryKey) ? "OpenLibrary" : "", "Manual");
    private bool IsMissingMyAnimeListId => string.IsNullOrWhiteSpace(Entry.MyAnimeListId);
    private List<string> CategoryLabels => SplitLabels(Entry.Category);
    private string VisibleCategoryLabel => CategoryLabels.Count == 0 ? "" : CompactCategoryLabel(CategoryLabels[0]);
    private bool HasHiddenCategories => CategoryLabels.Count > 1;
    private int HiddenCategoryCount => Math.Max(0, CategoryLabels.Count - 1);
    private string ChapterCountLabel => Entry.ChapterCount is null ? "Unknown" : Entry.ChapterCount.Value.ToString();
    private string VolumeCountLabel => Entry.VolumeCount is null ? "Unknown" : Entry.VolumeCount.Value.ToString();
    private string FirstYearLabel => Entry.FirstPublishYear is null ? "Unknown" : Entry.FirstPublishYear.Value.ToString();
    private string FormatLabel => FirstNonEmpty(Entry.MediaType, Entry.Category, "Unknown");
    private string PublishingLabel => FirstNonEmpty(Entry.PublishingStatus, "Unknown");
    private bool HasMangaDexLink => !string.IsNullOrWhiteSpace(Entry.MangaDexId);
    private bool IsMangaDexSyncOverdue => HasMangaDexLink
        && Entry.MangaDexLastSyncedAt is not null
        && Entry.MangaDexLastSyncedAt < DateTimeOffset.UtcNow.AddHours(-30);
    private string MangaDexSyncLabel => Entry.MangaDexLastSyncedAt is null
        ? "Not checked yet"
        : IsMangaDexSyncOverdue
        ? "Sync overdue"
        : Entry.MangaDexLastSyncedAt.Value.ToLocalTime().ToString("g");
    private string CachedChapterLabel => Entry.CachedChapterCount == 1 ? "1 chapter" : $"{Entry.CachedChapterCount} chapters";
    private string MangaDexPreferredLanguageLabel => !HasMangaDexLink
        ? "Unbound"
        : Entry.MangaDexPreferredLanguageLatestChapter is null
            ? "Checking"
            : $"Ch. {FormatChapter(Entry.MangaDexPreferredLanguageLatestChapter.Value)}";
    private string MangaUpdatesStatusLabel => string.IsNullOrWhiteSpace(Entry.MangaUpdatesId)
        ? "Unbound"
        : Entry.MangaUpdatesLatestChapter is null
            ? "Awaiting sync"
            : $"Ch. {FormatChapter(Entry.MangaUpdatesLatestChapter.Value)}";
    private decimal? SourceGap => Entry.MangaUpdatesLatestChapter is not null && Entry.MangaDexPreferredLanguageLatestChapter is not null
        ? Entry.MangaUpdatesLatestChapter.Value - Entry.MangaDexPreferredLanguageLatestChapter.Value
        : null;
    private string SourceGapHeading => SourceGap switch
    {
        null when string.IsNullOrWhiteSpace(Entry.MangaUpdatesId) => "Source check",
        null => "Source sync",
        <= 1m => "MangaDex coverage",
        _ => "MangaDex behind"
    };
    private string SourceGapValue => SourceGap switch
    {
        null when string.IsNullOrWhiteSpace(Entry.MangaUpdatesId) => "Unbound",
        null => "Checking",
        <= 1m => "In sync",
        _ => $"+{FormatChapter(SourceGap.Value)}"
    };
    private string SourceGapDetail => SourceGap switch
    {
        null when string.IsNullOrWhiteSpace(Entry.MangaUpdatesId) => "Auto-match pending",
        null => "MangaUpdates pending",
        <= 1m => $"Reference Ch. {FormatChapter(Entry.MangaUpdatesLatestChapter!.Value)}",
        _ => $"Reference Ch. {FormatChapter(Entry.MangaUpdatesLatestChapter!.Value)}"
    };
    private string SourceGapModalDetail => SourceGap is null
        ? SourceGapDetail
        : SourceGap <= 1m
            ? $"MangaDex and MangaUpdates are within one chapter. {SourceGapDetail}."
            : $"MangaDex is {FormatChapter(SourceGap.Value)} chapters behind MangaUpdates. {SourceGapDetail}.";
    private string SourceGapScheme => SourceGap switch
    {
        null => "secondary",
        <= 1m => "primary",
        < 10m => "warm",
        _ => "ink"
    };
    private string SourceGapClass => SourceGap switch
    {
        null => "",
        <= 1m => "mh-source-gap-good",
        < 10m => "mh-source-gap-warning",
        _ => "mh-source-gap-critical"
    };
    private string IdentityLabel => IsMissingMyAnimeListId ? "MAL ID missing" : $"MAL #{Entry.MyAnimeListId}";
    private bool HasExternalReaderLink => !HasMangaDexLink && IsHttpUrl(Entry.FallbackReaderUrl);
    private string ReaderLinksLabel => (HasMangaDexLink, HasExternalReaderLink, Entry.LocalSeriesId) switch
    {
        (true, _, not null) => "MangaDex + local",
        (true, _, null) => "MangaDex",
        (_, true, not null) => "External + local",
        (_, true, null) => "External link",
        (_, _, not null) => "Local files",
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
    private Task ManageCache() => OnManageCache.InvokeAsync(Entry);
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

    private static string FormatChapter(decimal chapter) => chapter.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string SourceName(string source) => source.ToLowerInvariant() switch
    {
        "myanimelist" => "MAL",
        "openlibrary" => "OpenLibrary",
        _ => source
    };

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
