using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Design;

public partial class MangaBentoTile
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public int ColSpan { get; set; } = 1;
    [Parameter] public int RowSpan { get; set; } = 1;
    [Parameter] public string Scheme { get; set; } = "primary";
    [Parameter] public string CornerIcon { get; set; } = "";
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public EventCallback OnClick { get; set; }

    private bool Clickable => OnClick.HasDelegate;
    private string Role => Clickable ? "button" : "";
    private string TileClass => $"mh-bento-tile mh-bento-scheme-{Scheme} {(Clickable ? "is-clickable" : "")} {Class}".Trim();
}
