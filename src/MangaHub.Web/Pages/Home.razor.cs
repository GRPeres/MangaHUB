using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using MangaHub.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Pages;

public partial class Home : IDisposable
{
    [Inject] private AuthSessionService Auth { get; set; } = default!;
    [Inject] private MangaApiService MangaApi { get; set; } = default!;
    [Inject] private CatalogApiService CatalogApi { get; set; } = default!;
    [Inject] private ShelfApiService ShelfApi { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private UserResponse? currentUser;
    private bool isLoading;
    private List<MangaEntryResponse> shelf = [];
    private List<CatalogMangaResponse> catalog = [];
    private List<MangaEntryResponse> newReleases = [];
    private List<MangaEntryResponse> planned = [];
    private MangaEntryResponse? continueReading;
    private List<CatalogMangaResponse> recommendations = [];
    private List<MangaEntryResponse> pendingRatings = [];
    private HashSet<Guid> savingRatings = [];
    private string[] statusLabels = ["Reading", "Planned", "Done"];
    private double[] statusData = [];

    protected override async Task OnInitializedAsync()
    {
        Auth.Changed += OnAuthChanged;
        currentUser = await Auth.GetCurrentUserAsync();
        await LoadDashboardAsync();
    }

    private async Task LoadDashboardAsync()
    {
        if (currentUser is null)
        {
            return;
        }

        isLoading = true;
        try
        {
            var shelfTask = MangaApi.GetMangaEntriesAsync();
            var catalogTask = CatalogApi.GetCatalogAsync(language: currentUser.PreferredLanguage);
            await Task.WhenAll(shelfTask, catalogTask);
            shelf = shelfTask.Result;
            catalog = catalogTask.Result;

            newReleases = shelf.Where(IsReadingWithNewChapters).OrderByDescending(ReleaseGap).ThenBy(entry => entry.Title).ToList();
            planned = shelf.Where(entry => string.Equals(entry.ReadingStatus, "planned", StringComparison.OrdinalIgnoreCase)).OrderBy(entry => entry.Title).ToList();
            continueReading = shelf.FirstOrDefault(entry => string.Equals(entry.ReadingStatus, "reading", StringComparison.OrdinalIgnoreCase))
                ?? planned.FirstOrDefault();
            recommendations = catalog.Where(entry => !entry.IsInMyShelf).OrderBy(entry => entry.Title).Take(3).ToList();
            pendingRatings = shelf
                .Where(entry => string.Equals(entry.ReadingStatus, "done", StringComparison.OrdinalIgnoreCase) && entry.Score is null)
                .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            statusData =
            [
                shelf.Count(entry => string.Equals(entry.ReadingStatus, "reading", StringComparison.OrdinalIgnoreCase)),
                planned.Count,
                shelf.Count(entry => string.Equals(entry.ReadingStatus, "done", StringComparison.OrdinalIgnoreCase))
            ];
        }
        finally
        {
            isLoading = false;
        }
    }

    private void OpenLogin() => Auth.RequestLogin("Log in to open your reading dashboard.");
    private void GoLibrary() => Navigation.NavigateTo("library");
    private void GoCatalog() => Navigation.NavigateTo("catalog");
    private void GoNewReleases() => Navigation.NavigateTo("library?availability=new");
    private void GoPlanned() => Navigation.NavigateTo("library?status=planned");
    private void OpenContinueReading() => Navigation.NavigateTo("library");

    private async Task SetScore(MangaEntryResponse entry, int score)
    {
        if (!savingRatings.Add(entry.Id)) return;
        try
        {
            var request = new AddToShelfRequest(entry.Id, entry.ReadingStatus, entry.CurrentChapter, score, entry.Category, entry.Summary, entry.Notes);
            var updated = await ShelfApi.UpdateShelfAsync(entry.Id, request);
            if (updated is null) return;

            var index = shelf.FindIndex(item => item.Id == entry.Id);
            if (index >= 0) shelf[index] = updated;
            pendingRatings.RemoveAll(item => item.Id == entry.Id);
        }
        finally
        {
            savingRatings.Remove(entry.Id);
        }
    }

    private void OnAuthChanged()
    {
        currentUser = Auth.CurrentUser;
        _ = InvokeAsync(async () =>
        {
            await LoadDashboardAsync();
            StateHasChanged();
        });
    }

    private static bool IsReadingWithNewChapters(MangaEntryResponse entry) =>
        string.Equals(entry.ReadingStatus, "reading", StringComparison.OrdinalIgnoreCase)
        && entry.MangaDexPreferredLanguageLatestChapter is { } latest
        && (latest > ParseChapter(entry.CurrentChapter)
            || (latest == ParseChapter(entry.CurrentChapter) && !entry.IsRead));

    private static decimal ReleaseGap(MangaEntryResponse entry)
    {
        var gap = Math.Max(0, (entry.MangaDexPreferredLanguageLatestChapter ?? 0) - ParseChapter(entry.CurrentChapter));
        return gap == 0 && !entry.IsRead && entry.MangaDexPreferredLanguageLatestChapter == ParseChapter(entry.CurrentChapter) ? 1 : gap;
    }
    private static decimal ParseChapter(string value) => decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static string DisplayChapter(string value) => string.IsNullOrWhiteSpace(value) ? "not started" : value;
    private static string LatestLabel(MangaEntryResponse entry) => entry.MangaDexPreferredLanguageLatestChapter is { } latest ? $"Latest available: {latest:0.###}" : "No language-specific release data yet";
    private static string ReleaseLabel(MangaEntryResponse entry) => $"+{ReleaseGap(entry):0.###} chapter{(ReleaseGap(entry) == 1 ? "" : "s")}";
    private string PreferredLanguagesLabel => string.Join(" / ", (currentUser?.PreferredLanguage ?? "en").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(language => language.ToUpperInvariant()));
    private static string EntryMeta(CatalogMangaResponse entry) => string.Join(" · ", new[] { entry.MediaType, entry.FirstPublishYear?.ToString() }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public void Dispose() => Auth.Changed -= OnAuthChanged;
}
