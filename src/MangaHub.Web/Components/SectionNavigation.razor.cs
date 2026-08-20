using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace MangaHub.Web.Components;

public sealed record SectionNavigationItem(string Key, string Label, string Icon, Color ButtonColor = Color.Primary, bool IsPrimary = false);

public partial class SectionNavigation
{
    [Parameter, EditorRequired] public IReadOnlyList<SectionNavigationItem> Items { get; set; } = [];
    [Parameter] public string ActiveKey { get; set; } = "";
    [Parameter] public EventCallback<string> OnSelect { get; set; }
    [Parameter] public string AriaLabel { get; set; } = "Sections";
    [Parameter] public bool CollapseOnOverflow { get; set; }
    [Parameter] public bool Expanded { get; set; }
    [Parameter] public EventCallback<bool> ExpandedChanged { get; set; }

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private ElementReference navigationElement;
    private DotNetObjectReference<SectionNavigation>? overflowReference;
    private bool isOverflowing;

    private string NavigationClass => $"mh-section-nav{(CollapseOnOverflow ? " is-collapsible" : "")}{(isOverflowing ? " is-overflowing" : "")}{(Expanded ? " is-expanded" : "")}";
    private bool IsActive(SectionNavigationItem item) => string.Equals(ActiveKey, item.Key, StringComparison.Ordinal);
    private string ButtonClass(SectionNavigationItem item) => $"mh-section-nav-button{(item.IsPrimary ? " mh-section-nav-primary" : " mh-section-nav-optional")}";

    private Task SelectAsync(string key) => OnSelect.InvokeAsync(key);
    private Task ToggleAsync() => ExpandedChanged.InvokeAsync(!Expanded);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!CollapseOnOverflow)
        {
            return;
        }

        if (firstRender)
        {
            overflowReference = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("mangaHubSectionNavigation.observe", navigationElement, overflowReference);
            return;
        }

        await JS.InvokeVoidAsync("mangaHubSectionNavigation.measure", navigationElement);
    }

    [JSInvokable]
    public Task SetOverflowAsync(bool value)
    {
        if (isOverflowing == value)
        {
            return Task.CompletedTask;
        }

        isOverflowing = value;
        return InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        if (overflowReference is null)
        {
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("mangaHubSectionNavigation.disconnect", navigationElement);
        }
        catch (JSException)
        {
            // The browser may already be disposing the WebAssembly runtime.
        }

        overflowReference.Dispose();
    }
}
