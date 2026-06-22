using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Pages.Shelf;

public partial class ShelfAddModal
{
    [Inject] private CatalogApiService CatalogApi { get; set; } = default!;
    [Inject] private ShelfApiService ShelfApi { get; set; } = default!;

    [Parameter] public bool Open { get; set; }
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }

    private string catalogQuery = "";
    private string catalogMessage = "";
    private Severity catalogSeverity = Severity.Info;
    private List<CatalogMangaResponse> catalogResults = [];
    private CatalogMangaResponse? selectedCatalogManga;
    private int shelfWizardStep = 1;
    private bool IsDoneStatus => string.Equals(readingStatus, "done", StringComparison.OrdinalIgnoreCase);
    private string readingStatus = "planned";
    private string currentChapter = "";
    private int? score;
    private string category = "";
    private string summary = "";
    private string notes = "";

    protected override async Task OnParametersSetAsync()
    {
        if (Open && catalogResults.Count == 0)
        {
            catalogResults = await CatalogApi.GetCatalogAsync();
        }
    }

    private async Task SearchCatalog()
    {
        catalogResults = await CatalogApi.GetCatalogAsync(catalogQuery);
        catalogSeverity = catalogResults.Count == 0 ? Severity.Warning : Severity.Success;
        catalogMessage = catalogResults.Count == 0 ? "No catalog manga matched your search." : $"Found {catalogResults.Count} catalog entries.";
    }

    private void SelectCatalogManga(CatalogMangaResponse item)
    {
        selectedCatalogManga = item;
        shelfWizardStep = 2;
        catalogMessage = "";
        readingStatus = "planned";
        currentChapter = "";
        score = null;
        category = "";
        summary = "";
        notes = "";
    }

    private void BackToCatalog()
    {
        shelfWizardStep = 1;
        selectedCatalogManga = null;
        catalogMessage = "";
    }

    private async Task AddSelectedToShelf()
    {
        if (selectedCatalogManga is null)
        {
            catalogSeverity = Severity.Warning;
            catalogMessage = "Choose a manga first.";
            shelfWizardStep = 1;
            return;
        }

        var request = new AddToShelfRequest(
            selectedCatalogManga.Id,
            readingStatus,
            readingStatus == "planned" ? "" : currentChapter,
            IsDoneStatus ? score : null,
            IsDoneStatus ? category : "",
            IsDoneStatus ? summary : "",
            notes);

        var created = await ShelfApi.AddToShelfAsync(request);
        catalogSeverity = created is null ? Severity.Error : Severity.Success;
        catalogMessage = created is null ? "Could not add manga to your shelf." : $"Added {created.Title} to your shelf.";
        catalogResults = await CatalogApi.GetCatalogAsync(catalogQuery);
        if (created is not null)
        {
            Reset();
            await OnSaved.InvokeAsync();
            await Close();
        }
    }

    private async Task Close()
    {
        Reset();
        await OpenChanged.InvokeAsync(false);
    }

    private void Reset()
    {
        shelfWizardStep = 1;
        selectedCatalogManga = null;
        catalogMessage = "";
        readingStatus = "planned";
        currentChapter = "";
        score = null;
        category = "";
        summary = "";
        notes = "";
    }
}
