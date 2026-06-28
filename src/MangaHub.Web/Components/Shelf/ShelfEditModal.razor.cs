using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Components.Shelf;

public partial class ShelfEditModal
{
    [Inject] private ShelfApiService ShelfApi { get; set; } = default!;

    [Parameter] public MangaEntryResponse? Entry { get; set; }
    [Parameter] public Guid? OwnerUserId { get; set; }
    [Parameter] public EventCallback<string> OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    private Guid? loadedEntryId;
    private string editStatus = "planned";
    private string editChapter = "";
    private int? editScore;
    private string editCategory = "";
    private string editSummary = "";
    private string editNotes = "";
    private string message = "";
    private Severity messageSeverity = Severity.Warning;
    private bool isSaving;
    private bool isRemoving;
    private bool IsBusy => isSaving || isRemoving;

    protected override void OnParametersSet()
    {
        if (Entry is null || loadedEntryId == Entry.Id)
        {
            return;
        }

        loadedEntryId = Entry.Id;
        editStatus = Entry.ReadingStatus;
        editChapter = Entry.CurrentChapter;
        editScore = Entry.Score;
        editCategory = Entry.Category;
        editSummary = Entry.Summary;
        editNotes = Entry.Notes;
        message = "";
        isSaving = false;
        isRemoving = false;
    }

    private async Task SaveEdit()
    {
        if (Entry is null || IsBusy)
        {
            return;
        }

        isSaving = true;
        messageSeverity = Severity.Info;
        message = "Saving shelf changes...";
        try
        {
            var request = new AddToShelfRequest(Entry.Id, editStatus, editChapter, editScore, editCategory, editSummary, editNotes);
            var updated = await ShelfApi.UpdateShelfAsync(Entry.Id, request, OwnerUserId);
            if (updated is null)
            {
                messageSeverity = Severity.Error;
                message = "Could not save shelf changes.";
                return;
            }

            messageSeverity = Severity.Success;
            message = $"Saved {updated.Title}.";
            await OnSaved.InvokeAsync($"Updated {updated.Title}.");
        }
        finally
        {
            isSaving = false;
        }
    }

    private async Task RemoveEntry()
    {
        if (Entry is null || IsBusy)
        {
            return;
        }

        isRemoving = true;
        messageSeverity = Severity.Info;
        message = "Removing shelf entry...";
        try
        {
            var removed = await ShelfApi.RemoveShelfAsync(Entry.Id, OwnerUserId);
            if (!removed)
            {
                messageSeverity = Severity.Error;
                message = "Could not remove shelf entry.";
                return;
            }

            messageSeverity = Severity.Success;
            message = $"Removed {Entry.Title}.";
            await OnSaved.InvokeAsync($"Removed {Entry.Title} from shelf.");
        }
        finally
        {
            isRemoving = false;
        }
    }

    private async Task Close()
    {
        if (IsBusy)
        {
            return;
        }

        loadedEntryId = null;
        message = "";
        await OnClosed.InvokeAsync();
    }
}
