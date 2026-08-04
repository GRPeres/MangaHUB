using Microsoft.AspNetCore.Components;
using MangaHub.Web.API.DTOs;

namespace MangaHub.Web.Components.Catalog;

public partial class CatalogEntryCardMobile
{
    [Parameter, EditorRequired] public CatalogMangaResponse Entry { get; set; } = default!;
    [Parameter] public EventCallback<CatalogMangaResponse> OnEdit { get; set; }
    [Parameter] public EventCallback<CatalogMangaResponse> OnManageCache { get; set; }

    private bool metadataOpen;
    private bool detailsOpen;
    private bool IsMissingMyAnimeListId => string.IsNullOrWhiteSpace(Entry.MyAnimeListId);
    private List<string> CategoryLabels => SplitLabels(Entry.Category);
    private string VisibleCategoryLabel => CategoryLabels.Count == 0 ? "" : CompactCategoryLabel(CategoryLabels[0]);
    private bool HasHiddenCategories => CategoryLabels.Count > 1;
    private int HiddenCategoryCount => Math.Max(0, CategoryLabels.Count - 1);
    private bool HasMangaDexLink => !string.IsNullOrWhiteSpace(Entry.MangaDexId);
    private string CachedChapterLabel => Entry.CachedChapterCount == 1 ? "1 chapter" : $"{Entry.CachedChapterCount} chapters";
    private string MangaDexPreferredLanguageLabel => !HasMangaDexLink ? "Unbound" : Entry.MangaDexPreferredLanguageLatestChapter is null ? "Checking" : $"Ch. {FormatChapter(Entry.MangaDexPreferredLanguageLatestChapter.Value)}";
    private string MangaUpdatesStatusLabel => string.IsNullOrWhiteSpace(Entry.MangaUpdatesId) ? "Unbound" : Entry.MangaUpdatesLatestChapter is null ? "Awaiting sync" : $"Ch. {FormatChapter(Entry.MangaUpdatesLatestChapter.Value)}";
    private decimal? SourceGap => Entry.MangaUpdatesLatestChapter is not null && Entry.MangaDexPreferredLanguageLatestChapter is not null ? Entry.MangaUpdatesLatestChapter.Value - Entry.MangaDexPreferredLanguageLatestChapter.Value : null;
    private string SourceGapHeading => SourceGap switch { null when string.IsNullOrWhiteSpace(Entry.MangaUpdatesId) => "Source check", null => "Source sync", <= 1m => "MangaDex coverage", _ => "MangaDex behind" };
    private string SourceGapValue => SourceGap switch { null when string.IsNullOrWhiteSpace(Entry.MangaUpdatesId) => "Unbound", null => "Checking", <= 1m => "In sync", _ => $"+{FormatChapter(SourceGap.Value)}" };
    private string SourceGapDetail => SourceGap is null ? (string.IsNullOrWhiteSpace(Entry.MangaUpdatesId) ? "Auto-match pending" : "MangaUpdates pending") : $"Reference Ch. {FormatChapter(Entry.MangaUpdatesLatestChapter!.Value)}";
    private string SourceGapScheme => SourceGap switch { null => "secondary", <= 1m => "primary", < 10m => "warm", _ => "ink" };
    private string SourceGapClass => SourceGap switch { null => "", <= 1m => "mh-mobile-catalog-gap-good", < 10m => "mh-mobile-catalog-gap-warning", _ => "mh-mobile-catalog-gap-critical" };
    private string FormatLabel => FirstNonEmpty(Entry.MediaType, Entry.Category, "Unknown");
    private string MetadataTitleId => $"mobile-catalog-metadata-{Entry.Id:N}";

    private Task Edit() => OnEdit.InvokeAsync(Entry);
    private Task ManageCache() => OnManageCache.InvokeAsync(Entry);
    private void OpenMetadata() => metadataOpen = true;
    private void CloseMetadata() => metadataOpen = false;
    private void ToggleDetails() => detailsOpen = !detailsOpen;
    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    private static List<string> SplitLabels(string value) => (value ?? "").Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(label => !string.IsNullOrWhiteSpace(label)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static string CompactCategoryLabel(string value) { var trimmed = value.Trim(); if (trimmed.Length <= 14 && !trimmed.Any(char.IsWhiteSpace)) return trimmed; var firstWord = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? trimmed; return firstWord.Length <= 12 ? $"{firstWord}..." : $"{firstWord[..12]}..."; }
    private static string FormatChapter(decimal chapter) => chapter.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
