using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace MangaHub.Web.Components.Cards;

public partial class MangaInfoBlock
{
    [Parameter, EditorRequired] public string Label { get; set; } = "";
    [Parameter, EditorRequired] public string Value { get; set; } = "";
    [Parameter] public string Icon { get; set; } = MudBlazor.Icons.Material.Filled.Label;
    [Parameter] public string Tone { get; set; } = "neutral";
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool Active { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }

    private bool Clickable => OnClick.HasDelegate;
    private string BlockClass => $"mh-info-block mh-info-{Tone} {Class} {(Active ? "is-active" : "")} {(Clickable ? "is-clickable" : "")}";

    private Task HandleClick() => Clickable ? OnClick.InvokeAsync() : Task.CompletedTask;

    private Task HandleKeyDown(KeyboardEventArgs args)
    {
        if (!Clickable || args.Key is not ("Enter" or " "))
        {
            return Task.CompletedTask;
        }

        return OnClick.InvokeAsync();
    }
}
