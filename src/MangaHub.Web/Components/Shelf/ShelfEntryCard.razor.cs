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

    private bool metadataOpen;
    private string StatusLabel => string.IsNullOrWhiteSpace(Entry.ReadingStatus) ? "planned" : Entry.ReadingStatus;
    private string GenreLabel => FirstNonEmpty(Entry.Category, Entry.CatalogCategory);
    private List<string> GenreLabels => SplitLabels(GenreLabel);
    private string VisibleGenreLabel => GenreLabels.Count == 0 ? "" : CompactCategoryLabel(GenreLabels[0]);
    private bool HasHiddenGenres => GenreLabels.Count > 1;
    private int HiddenGenreCount => Math.Max(0, GenreLabels.Count - 1);
    private string DescriptionText => FirstNonEmpty(Entry.Summary, Entry.Description);
    private string ScoreLabel => Entry.Score is null ? "Not scored" : $"{Entry.Score}/5";
    private string ReadSourceLabel => !string.IsNullOrWhiteSpace(Entry.MangaDexUrl)
        ? "MangaDex"
        : Entry.LocalSeriesId is not null ? "Local" : "Unlinked";
    private string CurrentChapterValue => string.IsNullOrWhiteSpace(Entry.CurrentChapter) ? "Not started" : $"Ch. {Entry.CurrentChapter}";
    private string LatestChapterValue => Entry.ChapterCount is null ? "Unknown" : $"Ch. {Entry.ChapterCount}";
    private string FormatLabel => FirstNonEmpty(Entry.MediaType, GenreLabel, "Unknown");
    private string FirstYearLabel => Entry.FirstPublishYear is null ? "Unknown" : Entry.FirstPublishYear.Value.ToString();
    private string VolumeCountLabel => Entry.VolumeCount is null ? "Unknown" : Entry.VolumeCount.Value.ToString();
    private string NextChapterValue => TryGetCurrentChapterNumber(out var currentChapter)
        ? $"Ch. {currentChapter + 1}"
        : StatusLabel.Equals("done", StringComparison.OrdinalIgnoreCase) ? "Complete" : "Start";
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
    private string MetadataTitleId => $"shelf-metadata-{Entry.Id:N}";
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

    private bool TryGetCurrentChapterNumber(out int chapter)
    {
        chapter = 0;
        if (string.IsNullOrWhiteSpace(Entry.CurrentChapter))
        {
            return false;
        }

        var digits = new string(Entry.CurrentChapter
            .SkipWhile(value => !char.IsDigit(value))
            .TakeWhile(char.IsDigit)
            .ToArray());
        return int.TryParse(digits, out chapter);
    }
}
