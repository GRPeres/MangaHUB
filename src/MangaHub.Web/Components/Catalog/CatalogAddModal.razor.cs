using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Components.Catalog;

public partial class CatalogAddModal
{
    [Inject] private CatalogApiService CatalogApi { get; set; } = default!;
    [Inject] private OpenLibraryApiService OpenLibraryApi { get; set; } = default!;

    [Parameter] public bool Open { get; set; }
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }
    [Parameter] public List<SeriesResponse> LocalSeries { get; set; } = [];
    [Parameter] public EventCallback OnSaved { get; set; }

    private string title = "";
    private string authors = "";
    private string category = "";
    private string description = "";
    private string coverUrl = "";
    private string openLibraryKey = "";
    private int? firstPublishYear;
    private string mangaDexUrl = "";
    private string localSeriesIdText = "";
    private string message = "";
    private Severity messageSeverity = Severity.Info;
    private bool showOpenLibrary;
    private string openLibraryQuery = "";
    private string openLibraryMessage = "";
    private Severity openLibrarySeverity = Severity.Info;
    private bool isSearchingOpenLibrary;
    private List<OpenLibraryResult> openLibraryResults = [];

    private void ToggleOpenLibrary()
    {
        showOpenLibrary = !showOpenLibrary;
        if (showOpenLibrary && string.IsNullOrWhiteSpace(openLibraryQuery))
        {
            openLibraryQuery = title;
        }
    }

    private async Task SearchOpenLibrary()
    {
        if (string.IsNullOrWhiteSpace(openLibraryQuery))
        {
            openLibraryResults = [];
            openLibrarySeverity = Severity.Info;
            openLibraryMessage = "Type a title before searching OpenLibrary.";
            return;
        }

        isSearchingOpenLibrary = true;
        try
        {
            openLibraryResults = await OpenLibraryApi.SearchOpenLibraryAsync(openLibraryQuery);
            openLibrarySeverity = openLibraryResults.Count == 0 ? Severity.Warning : Severity.Success;
            openLibraryMessage = openLibraryResults.Count == 0
                ? "OpenLibrary returned no matches."
                : $"Found {openLibraryResults.Count} OpenLibrary matches.";
        }
        catch
        {
            openLibraryResults = [];
            openLibrarySeverity = Severity.Error;
            openLibraryMessage = "OpenLibrary search failed.";
        }
        finally
        {
            isSearchingOpenLibrary = false;
        }
    }

    private void ApplyOpenLibrary(OpenLibraryResult item)
    {
        title = item.Title;
        authors = item.Authors;
        category = item.Category;
        description = item.Description;
        coverUrl = item.CoverUrl;
        openLibraryKey = item.Key;
        firstPublishYear = item.FirstPublishYear;
        openLibrarySeverity = Severity.Success;
        openLibraryMessage = $"Filled the form from {item.Title}.";
    }

    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            messageSeverity = Severity.Warning;
            message = "Title is required.";
            return;
        }

        var created = await CatalogApi.CreateCatalogMangaAsync(BuildRequest());
        if (created is null)
        {
            messageSeverity = Severity.Error;
            message = "Catalog registration failed. Admin permissions are required.";
            return;
        }

        await OnSaved.InvokeAsync();
        Reset();
        await OpenChanged.InvokeAsync(false);
    }

    private MangaEntryRequest BuildRequest()
    {
        var localSeriesId = Guid.TryParse(localSeriesIdText, out var parsed) ? parsed : (Guid?)null;
        return new MangaEntryRequest(title, authors, category, description, coverUrl, openLibraryKey, firstPublishYear, "", mangaDexUrl, localSeriesId, "");
    }

    private async Task Close()
    {
        Reset();
        await OpenChanged.InvokeAsync(false);
    }

    private void Reset()
    {
        title = "";
        authors = "";
        category = "";
        description = "";
        coverUrl = "";
        openLibraryKey = "";
        firstPublishYear = null;
        mangaDexUrl = "";
        localSeriesIdText = "";
        message = "";
        showOpenLibrary = false;
        openLibraryQuery = "";
        openLibraryMessage = "";
        openLibraryResults = [];
    }
}
