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
    }

    private async Task SaveEdit()
    {
        if (Entry is null)
        {
            return;
        }

        var request = new AddToShelfRequest(Entry.Id, editStatus, editChapter, editScore, editCategory, editSummary, editNotes);
        var updated = await ShelfApi.UpdateShelfAsync(Entry.Id, request, OwnerUserId);
        if (updated is null)
        {
            messageSeverity = Severity.Error;
            message = "Could not save shelf changes.";
            return;
        }

        await OnSaved.InvokeAsync($"Updated {updated.Title}.");
    }

    private async Task RemoveEntry()
    {
        if (Entry is null)
        {
            return;
        }

        var removed = await ShelfApi.RemoveShelfAsync(Entry.Id, OwnerUserId);
        if (!removed)
        {
            messageSeverity = Severity.Error;
            message = "Could not remove shelf entry.";
            return;
        }

        await OnSaved.InvokeAsync($"Removed {Entry.Title} from shelf.");
    }

    private async Task Close()
    {
        loadedEntryId = null;
        message = "";
        await OnClosed.InvokeAsync();
    }
}
