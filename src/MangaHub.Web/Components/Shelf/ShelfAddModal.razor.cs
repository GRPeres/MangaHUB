using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Components.Shelf;

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
    private bool isSearchingCatalog;
    private bool isSaving;

    protected override async Task OnParametersSetAsync()
    {
        if (Open && catalogResults.Count == 0)
        {
            catalogResults = await CatalogApi.GetCatalogAsync();
        }
    }

    private async Task SearchCatalog()
    {
        if (isSearchingCatalog)
        {
            return;
        }

        isSearchingCatalog = true;
        catalogSeverity = Severity.Info;
        catalogMessage = "Searching catalog...";
        try
        {
            catalogResults = await CatalogApi.GetCatalogAsync(catalogQuery);
            catalogSeverity = catalogResults.Count == 0 ? Severity.Warning : Severity.Success;
            catalogMessage = catalogResults.Count == 0 ? "No catalog manga matched your search." : $"Found {catalogResults.Count} catalog entries.";
        }
        finally
        {
            isSearchingCatalog = false;
        }
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
        if (isSaving)
        {
            return;
        }

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

        isSaving = true;
        catalogSeverity = Severity.Info;
        catalogMessage = "Adding manga to your shelf...";
        try
        {
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
        finally
        {
            isSaving = false;
        }
    }

    private async Task Close()
    {
        if (isSaving)
        {
            return;
        }

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
        isSaving = false;
        isSearchingCatalog = false;
    }
}
