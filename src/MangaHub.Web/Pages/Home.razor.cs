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
    [Inject] private DashboardApiService DashboardApi { get; set; } = default!;
    [Inject] private ShelfApiService ShelfApi { get; set; } = default!;
    [Inject] private UsageApiService UsageApi { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private AppRefreshService Refreshes { get; set; } = default!;

    private UserResponse? currentUser;
    private bool isLoading;
    private HomeDashboardResponse? dashboard;
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
        Refreshes.Changed += OnAppRefresh;
        currentUser = await Auth.GetCurrentUserAsync();
        if (currentUser is not null)
        {
            isLoading = true;
            _ = InitializeDashboardAsync();
        }
    }

    private async Task InitializeDashboardAsync()
    {
        try
        {
            await LoadDashboardAsync();
        }
        catch
        {
            // The normal dashboard refresh path will retry after the next app event.
        }
        finally
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadDashboardAsync()
    {
        if (currentUser is null)
        {
            dashboard = null;
            usageDashboard = null;
            return;
        }

        isLoading = true;
        try
        {
            dashboard = await DashboardApi.GetAsync();
            if (currentUser.UsageAnalyticsEnabled)
            {
                _ = LoadUsageDashboardAsync();
            }
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task LoadUsageDashboardAsync()
    {
        try
        {
            usageDashboard = await UsageApi.GetDashboardAsync(30);
            BuildReadingActivityChart();
            await InvokeAsync(StateHasChanged);
        }
        catch
        {
            // Analytics should never block the useful reading dashboard.
        }
    }

    private void OpenLogin() => Auth.RequestLogin("Log in to open your reading dashboard.");
    private void GoLibrary() => Navigation.NavigateTo("library");
    private void GoCatalog() => Navigation.NavigateTo("admin/catalog");
    private void GoNewReleases() => Navigation.NavigateTo("library?availability=new");
    private void GoPlanned() => Navigation.NavigateTo("library/planned");
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

    private async Task SetScore(HomeDashboardMangaResponse entry, int score)
    {
        if (!savingRatings.Add(entry.Id)) return;
        try
        {
            var request = new AddToShelfRequest(entry.Id, entry.ReadingStatus, entry.CurrentChapter, score, entry.Category, entry.Summary, entry.Notes);
            var updated = await ShelfApi.UpdateShelfAsync(entry.Id, request);
            if (updated is null) return;

            dashboard = dashboard is null
                ? null
                : dashboard with { PendingRatings = dashboard.PendingRatings.Where(item => item.Id != entry.Id).ToList() };
            Refreshes.Notify(AppRefreshScope.Shelf | AppRefreshScope.Analytics);
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

    private void OnAppRefresh(AppRefreshScope scope)
    {
        if ((scope & (AppRefreshScope.Shelf | AppRefreshScope.Catalog | AppRefreshScope.Analytics)) == 0)
        {
            return;
        }

        _ = InvokeAsync(async () =>
        {
            await LoadDashboardAsync();
            StateHasChanged();
        });
    }

    public void Dispose()
    {
        Auth.Changed -= OnAuthChanged;
        Refreshes.Changed -= OnAppRefresh;
    }

    private static decimal ReleaseGap(HomeDashboardMangaResponse entry)
    {
        var current = decimal.TryParse(entry.CurrentChapter, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        var gap = Math.Max(0, (entry.MangaDexPreferredLanguageLatestChapter ?? 0) - current);
        return gap == 0 && !entry.IsRead && entry.MangaDexPreferredLanguageLatestChapter == current ? 1 : gap;
    }
    private static string DisplayChapter(string value) => string.IsNullOrWhiteSpace(value) ? "not started" : value;
    private static string LatestLabel(HomeDashboardMangaResponse entry) => entry.MangaDexPreferredLanguageLatestChapter is { } latest ? $"Latest available: {latest:0.###}" : "No language-specific release data yet";
    private static string ReleaseLabel(HomeDashboardMangaResponse entry) => $"+{ReleaseGap(entry):0.###} chapter{(ReleaseGap(entry) == 1 ? "" : "s")}";
    private string PreferredLanguagesLabel => string.Join(" / ", (currentUser?.PreferredLanguage ?? "en").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(language => language.ToUpperInvariant()));
    private static string EntryMeta(HomeDashboardMangaResponse entry) => string.Join(" · ", new[] { entry.MediaType, entry.FirstPublishYear?.ToString() }.Where(value => !string.IsNullOrWhiteSpace(value)));

}
