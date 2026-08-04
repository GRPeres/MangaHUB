using Microsoft.AspNetCore.Components;
using MangaHub.Web.API.DTOs;
using MudBlazor;
using System.Globalization;

namespace MangaHub.Web.Components.Shelf;

public partial class ShelfEntryCardMobile
{
    [Parameter, EditorRequired] public MangaEntryResponse Entry { get; set; } = default!;
    [Parameter] public string ActiveStatusFilter { get; set; } = "";
    [Parameter] public bool CanRead { get; set; }
    [Parameter] public EventCallback<MangaEntryResponse> OnEdit { get; set; }
    [Parameter] public EventCallback<Guid> OnRead { get; set; }
    [Parameter] public EventCallback<string> OnStatusFilter { get; set; }
    [Parameter] public EventCallback<int?> OnScoreChanged { get; set; }

    private bool detailsOpen;
    private bool metadataOpen;
    private bool isSavingScore;
    private int? selectedScore;
    private string StatusLabel => string.IsNullOrWhiteSpace(Entry.ReadingStatus) ? "planned" : Entry.ReadingStatus;
    private string GenreLabel => FirstNonEmpty(Entry.Category, Entry.CatalogCategory);
    private List<string> GenreLabels => SplitLabels(GenreLabel);
    private string VisibleGenreLabel => GenreLabels.Count == 0 ? "" : CompactCategoryLabel(GenreLabels[0]);
    private bool HasHiddenGenres => GenreLabels.Count > 1;
    private int HiddenGenreCount => Math.Max(0, GenreLabels.Count - 1);
    private string DescriptionText => FirstNonEmpty(Entry.Summary, Entry.Description);
    private string ScoreLabel => Entry.Score is null ? "Not scored" : $"{Entry.Score}/5";
    private bool HasMangaDexLink => !string.IsNullOrWhiteSpace(Entry.MangaDexId);
    private bool HasExternalReaderLink => IsHttpUrl(Entry.FallbackReaderUrl);
    private bool HasMangaHubReader => Entry.LocalSeriesId is not null || HasMangaDexLink;
    private bool OpensExternalReader => HasExternalReaderLink && (Entry.ReaderPreference == "external" || !HasMangaHubReader);
    private bool ShowsExternalReaderAction => !OpensExternalReader && Entry.ReaderPreference == "hybrid" && HasExternalReaderLink;
    private string CurrentChapterValue => string.IsNullOrWhiteSpace(Entry.CurrentChapter) ? "Not started" : $"Ch. {Entry.CurrentChapter}";
    private decimal? PreferredLatestChapter => Entry.MangaDexPreferredLanguageLatestChapter;
    private string LatestChapterValue => PreferredLatestChapter is not null ? $"Ch. {PreferredLatestChapter.Value:0.###}" : Entry.ChapterCount is null ? "Unknown" : $"Ch. {Entry.ChapterCount}";
    private int NewChapterCount => HasMangaDexLink && PreferredLatestChapter is not null && TryGetCurrentChapterNumber(out var currentChapter) ? Math.Max(0, (int)Math.Ceiling(PreferredLatestChapter.Value - currentChapter)) : 0;
    private bool HasNewChapters => !StatusLabel.Equals("done", StringComparison.OrdinalIgnoreCase) && NewChapterCount > 0;
    private string CardClass => HasNewChapters ? "mh-mobile-shelf-card mh-mobile-shelf-has-release" : "mh-mobile-shelf-card";
    private string ProgressHint => !HasMangaDexLink ? "MangaDex sync unavailable" : HasNewChapters ? $"{NewChapterCount} new chapter{(NewChapterCount == 1 ? "" : "s")}" : $"Newest {LatestChapterValue}";
    private string ProgressScheme => HasNewChapters ? "release" : "secondary";
    private string StatusScheme => StatusLabel.ToLowerInvariant() switch { "reading" => "deep", "done" => "soft", "paused" => "warm", "planned" => "secondary", "dropped" => "ink", _ => "primary" };
    private string MetadataTitleId => $"mobile-shelf-metadata-{Entry.Id:N}";

    protected override void OnParametersSet() => selectedScore = Entry.Score;
    private Task Edit() => OnEdit.InvokeAsync(Entry);
    private Task Read() => OnRead.InvokeAsync(Entry.Id);
    private Task FilterByStatus() => OnStatusFilter.InvokeAsync(StatusLabel);
    private void ToggleDetails() => detailsOpen = !detailsOpen;
    private void OpenMetadata() => metadataOpen = true;
    private void CloseMetadata() => metadataOpen = false;
    private bool IsStarSelected(int score) => selectedScore is not null && score <= selectedScore;

    private async Task SetScore(int score)
    {
        if (isSavingScore) return;
        var previousScore = selectedScore;
        selectedScore = selectedScore == score ? null : score;
        isSavingScore = true;
        try { await OnScoreChanged.InvokeAsync(selectedScore); }
        catch { selectedScore = previousScore; throw; }
        finally { isSavingScore = false; }
    }

    private bool TryGetCurrentChapterNumber(out decimal chapter)
    {
        chapter = 0;
        if (string.IsNullOrWhiteSpace(Entry.CurrentChapter)) return false;
        var chapterText = new string(Entry.CurrentChapter.SkipWhile(value => !char.IsDigit(value)).TakeWhile(value => char.IsDigit(value) || value is '.' or ',').ToArray()).Replace(',', '.');
        return decimal.TryParse(chapterText, NumberStyles.Number, CultureInfo.InvariantCulture, out chapter);
    }

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    private static List<string> SplitLabels(string value) => (value ?? "").Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(label => !string.IsNullOrWhiteSpace(label)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static string CompactCategoryLabel(string value) { var trimmed = value.Trim(); if (trimmed.Length <= 14 && !trimmed.Any(char.IsWhiteSpace)) return trimmed; var firstWord = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? trimmed; return firstWord.Length <= 12 ? $"{firstWord}..." : $"{firstWord[..12]}..."; }
    private static bool IsHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
