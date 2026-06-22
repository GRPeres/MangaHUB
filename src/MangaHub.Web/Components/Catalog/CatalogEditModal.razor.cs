using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Components.Catalog;

public partial class CatalogEditModal
{
    [Inject] private CatalogApiService CatalogApi { get; set; } = default!;
    [Inject] private OpenLibraryApiService OpenLibraryApi { get; set; } = default!;

    [Parameter] public CatalogMangaResponse? Entry { get; set; }
    [Parameter] public List<SeriesResponse> LocalSeries { get; set; } = [];
    [Parameter] public EventCallback<CatalogMangaResponse> OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    private Guid? loadedEntryId;
    private string editTitle = "";
    private string editAuthors = "";
    private string editCategory = "";
    private string editDescription = "";
    private string editCoverUrl = "";
    private string editOpenLibraryKey = "";
    private int? editFirstPublishYear;
    private string editMangaDexUrl = "";
    private string editLocalSeriesIdText = "";
    private string message = "";
    private Severity messageSeverity = Severity.Info;
    private bool showOpenLibrary;
    private string openLibraryQuery = "";
    private string openLibraryMessage = "";
    private Severity openLibrarySeverity = Severity.Info;
    private bool isSearchingOpenLibrary;
    private List<OpenLibraryResult> openLibraryResults = [];

    protected override void OnParametersSet()
    {
        if (Entry is null || loadedEntryId == Entry.Id)
        {
            return;
        }

        loadedEntryId = Entry.Id;
        editTitle = Entry.Title;
        editAuthors = Entry.Authors;
        editCategory = Entry.Category;
        editDescription = Entry.Description;
        editCoverUrl = Entry.CoverUrl;
        editOpenLibraryKey = Entry.OpenLibraryKey;
        editFirstPublishYear = Entry.FirstPublishYear;
        editMangaDexUrl = Entry.MangaDexUrl;
        editLocalSeriesIdText = Entry.LocalSeriesId?.ToString() ?? "";
        openLibraryQuery = Entry.Title;
        openLibraryResults = [];
        openLibraryMessage = "";
        message = "";
        showOpenLibrary = false;
    }

    private void ToggleOpenLibrary() => showOpenLibrary = !showOpenLibrary;

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

    private void ApplyOpenLibraryMetadata(OpenLibraryResult item)
    {
        editTitle = item.Title;
        editAuthors = item.Authors;
        editCategory = item.Category;
        editDescription = item.Description;
        editCoverUrl = item.CoverUrl;
        editOpenLibraryKey = item.Key;
        editFirstPublishYear = item.FirstPublishYear;
        openLibrarySeverity = Severity.Success;
        openLibraryMessage = $"Filled the form from {item.Title}.";
    }

    private async Task SaveCatalog()
    {
        if (Entry is null)
        {
            return;
        }

        var updated = await CatalogApi.UpdateCatalogMangaAsync(Entry.Id, BuildRequest());
        if (updated is null)
        {
            messageSeverity = Severity.Error;
            message = "Could not save catalog metadata.";
            return;
        }

        await OnSaved.InvokeAsync(updated);
    }

    private MangaEntryRequest BuildRequest()
    {
        var localSeriesId = Guid.TryParse(editLocalSeriesIdText, out var parsed) ? parsed : (Guid?)null;
        return new MangaEntryRequest(editTitle, editAuthors, editCategory, editDescription, editCoverUrl, editOpenLibraryKey, editFirstPublishYear, "", editMangaDexUrl, localSeriesId, "");
    }

    private async Task Close()
    {
        loadedEntryId = null;
        await OnClosed.InvokeAsync();
    }
}
