using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Components;

public sealed record SectionNavigationItem(string Key, string Label, string Icon, Color ButtonColor = Color.Primary, bool IsPrimary = false);

public partial class SectionNavigation
{
    [Parameter, EditorRequired] public IReadOnlyList<SectionNavigationItem> Items { get; set; } = [];
    [Parameter] public string ActiveKey { get; set; } = "";
    [Parameter] public EventCallback<string> OnSelect { get; set; }
    [Parameter] public string AriaLabel { get; set; } = "Sections";
    [Parameter] public bool CollapseOnMobile { get; set; }
    [Parameter] public bool Expanded { get; set; }
    [Parameter] public EventCallback<bool> ExpandedChanged { get; set; }

    private string NavigationClass => $"mh-section-nav{(CollapseOnMobile ? " is-collapsible" : "")}{(Expanded ? " is-expanded" : "")}";
    private bool IsActive(SectionNavigationItem item) => string.Equals(ActiveKey, item.Key, StringComparison.Ordinal);
    private string ButtonClass(SectionNavigationItem item) => $"mh-section-nav-button{(item.IsPrimary ? " mh-section-nav-primary" : " mh-section-nav-optional")}";

    private Task SelectAsync(string key) => OnSelect.InvokeAsync(key);
    private Task ToggleAsync() => ExpandedChanged.InvokeAsync(!Expanded);
}
