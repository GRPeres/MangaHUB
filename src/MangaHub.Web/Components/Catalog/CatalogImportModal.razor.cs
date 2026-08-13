using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Catalog;

public partial class CatalogImportModal
{
    [Parameter] public bool Open { get; set; }
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }
    [Parameter] public EventCallback OnImported { get; set; }
}
