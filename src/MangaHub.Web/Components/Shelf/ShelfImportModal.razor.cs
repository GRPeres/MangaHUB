using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Shelf;

public partial class ShelfImportModal
{
    [Parameter] public bool Open { get; set; }
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }
    [Parameter] public EventCallback OnImported { get; set; }
}
