using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace MangaHub.Web.Components.Design;

public partial class MangaBentoTile
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public int ColSpan { get; set; } = 1;
    [Parameter] public int RowSpan { get; set; } = 1;
    [Parameter] public string Scheme { get; set; } = "primary";
    [Parameter] public string CornerIcon { get; set; } = "";
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string AriaLabel { get; set; } = "";
    [Parameter] public EventCallback OnClick { get; set; }

    private bool Clickable => OnClick.HasDelegate;
    private string? Role => Clickable ? "button" : null;
    private string? TabIndex => Clickable ? "0" : null;
    private string TileClass => $"mh-bento-tile mh-bento-scheme-{Scheme} {(Clickable ? "is-clickable" : "")} {Class}".Trim();

    private async Task HandleKeyDown(KeyboardEventArgs args)
    {
        if (!Clickable || (args.Key is not "Enter" and not " "))
        {
            return;
        }

        await OnClick.InvokeAsync();
    }
}
