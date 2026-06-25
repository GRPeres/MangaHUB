using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Cards;

public partial class MangaBlockGrid
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string Variant { get; set; } = "standard";

    private string GridClass => $"mh-block-grid mh-block-grid-{Variant} {Class}";
}
