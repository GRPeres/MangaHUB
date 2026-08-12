using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using MangaHub.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Charts;

namespace MangaHub.Web.Pages;

public partial class Home : IDisposable
{
    [Inject] private AuthSessionService Auth { get; set; } = default!;
    [Inject] private MangaApiService MangaApi { get; set; } = default!;
    [Inject] private CatalogApiService CatalogApi { get; set; } = default!;
    [Inject] private ShelfApiService ShelfApi { get; set; } = default!;
    [Inject] private UsageApiService UsageApi { get; set; } = default!;
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
    private UsageDashboardResponse? usageDashboard;
    private string[] readingActivityLabels = [];
    private List<ChartSeries<double>> readingActivitySeries = [];
    private readonly LineChartOptions readingActivityChartOptions = new()
    {
        ChartPalette = ["#C4B5FD"]
    };

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
            var usageTask = currentUser.UsageAnalyticsEnabled ? UsageApi.GetDashboardAsync(30) : Task.FromResult<UsageDashboardResponse?>(null);
            await Task.WhenAll(shelfTask, catalogTask, usageTask);
            shelf = shelfTask.Result;
            catalog = catalogTask.Result;
            usageDashboard = usageTask.Result;

            newReleases = shelf.Where(IsReadingWithNewChapters).OrderByDescending(ReleaseGap).ThenBy(entry => entry.Title).ToList();
            planned = shelf.Where(entry => string.Equals(entry.ReadingStatus, "planned", StringComparison.OrdinalIgnoreCase)).OrderBy(entry => entry.Title).ToList();
            continueReading = shelf.FirstOrDefault(entry => string.Equals(entry.ReadingStatus, "reading", StringComparison.OrdinalIgnoreCase))
                ?? planned.FirstOrDefault();
            recommendations = catalog.Where(entry => !entry.IsInMyShelf).OrderBy(entry => entry.Title).Take(3).ToList();
            pendingRatings = shelf
                .Where(entry => string.Equals(entry.ReadingStatus, "done", StringComparison.OrdinalIgnoreCase) && entry.Score is null)
                .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            BuildReadingActivityChart();
        }
        finally
        {
            isLoading = false;
        }
    }

    private void OpenLogin() => Auth.RequestLogin("Log in to open your reading dashboard.");
    private void GoLibrary() => Navigation.NavigateTo("library");
    private void GoCatalog() => Navigation.NavigateTo("admin/catalog");
    private void GoNewReleases() => Navigation.NavigateTo("library?availability=new");
    private void GoPlanned() => Navigation.NavigateTo("library?status=planned");
    private void OpenContinueReading() => Navigation.NavigateTo("library");
    private void GoAccount() => Navigation.NavigateTo("account");

    private int ChaptersReadYesterday => usageDashboard?.Days.FirstOrDefault(day => day.Date == DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)))?.ChaptersCompleted ?? 0;
    private int WeeklyReaderSeconds => usageDashboard?.Days.Where(day => day.Date >= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6))).Sum(day => day.ReaderSeconds) ?? 0;
    private int WeeklyChapters => usageDashboard?.Days.Where(day => day.Date >= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6))).Sum(day => day.ChaptersCompleted) ?? 0;
    private string WeeklyReadingTime => WeeklyReaderSeconds switch
    {
        < 60 => "< 1 min",
        < 3600 => $"{WeeklyReaderSeconds / 60} min",
        _ => $"{WeeklyReaderSeconds / 3600.0:0.#} h"
    };

    private bool HasReadingActivity => readingActivitySeries.Count > 0 && readingActivitySeries[0].Data.Values.Any(value => value > 0);

    private void BuildReadingActivityChart()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-6));
        var days = Enumerable.Range(0, 7)
            .Select(offset => start.AddDays(offset))
            .ToList();
        var dailyChapters = usageDashboard?.Days
            .GroupBy(day => day.Date)
            .ToDictionary(group => group.Key, group => group.Sum(day => day.ChaptersCompleted))
            ?? [];

        readingActivityLabels = days.Select(day => day.ToString("ddd")).ToArray();
        readingActivitySeries =
        [
            new ChartSeries<double>
            {
                Name = "Chapters read",
                Data = new ChartData<double>(days.Select(day => (double)dailyChapters.GetValueOrDefault(day)).ToArray())
            }
        ];
    }

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
