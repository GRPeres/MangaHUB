using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Bento;

public partial class BentoPanel
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public string Class { get; set; } = "";

    private string PanelClass => $"mh-bento-panel {Class}";
}
