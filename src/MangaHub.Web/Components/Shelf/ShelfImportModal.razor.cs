using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;

namespace MangaHub.Web.Pages.Shelf;

public partial class ShelfImportModal
{
    [Inject] private ShelfApiService ShelfApi { get; set; } = default!;

    [Parameter] public bool Open { get; set; }
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }
    [Parameter] public EventCallback OnImported { get; set; }

    private string importMessage = "";
    private Severity importSeverity = Severity.Info;

    private async Task ImportShelfCsv(InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (file is null)
        {
            return;
        }

        try
        {
            using var stream = file.OpenReadStream(maxAllowedSize: 2 * 1024 * 1024);
            using var reader = new StreamReader(stream);
            var csv = await reader.ReadToEndAsync();
            var result = await ShelfApi.ImportShelfAsync(new ShelfImportRequest(csv, false));
            importSeverity = result is null ? Severity.Error : Severity.Success;
            importMessage = result is null
                ? "Import failed."
                : $"Imported {result.Imported} rows, created {result.CreatedCatalogEntries} catalog entries, updated {result.UpdatedShelfEntries}, skipped {result.Skipped}.";
            if (result?.Messages.Count > 0)
            {
                importMessage += " " + string.Join(" ", result.Messages);
            }

            await OnImported.InvokeAsync();
        }
        catch
        {
            importSeverity = Severity.Error;
            importMessage = "Could not read the CSV file.";
        }
    }

    private async Task Close()
    {
        importMessage = "";
        await OpenChanged.InvokeAsync(false);
    }
}
