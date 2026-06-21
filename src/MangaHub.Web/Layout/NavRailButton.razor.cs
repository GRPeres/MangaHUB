using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Layout;

public partial class NavRailButton : ComponentBase
{
    [Parameter] public string Route { get; set; } = "";
    [Parameter] public string Label { get; set; } = "";
    [Parameter] public string Icon { get; set; } = "";
    [Parameter] public bool Expanded { get; set; }
    [Parameter] public bool IsActive { get; set; }
    [Parameter] public EventCallback<string> OnNavigate { get; set; }

    private string ButtonClass => IsActive ? "mh-nav-button mh-nav-button-active" : "mh-nav-button";

    private Task HandleClick() => OnNavigate.InvokeAsync(Route);
}
