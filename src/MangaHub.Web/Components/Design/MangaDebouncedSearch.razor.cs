using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Components.Design;

public partial class MangaDebouncedSearch
{
    [Parameter] public string Value { get; set; } = "";
    [Parameter] public EventCallback<string> ValueChanged { get; set; }
    [Parameter] public EventCallback<string> OnSearch { get; set; }
    [Parameter] public string Label { get; set; } = "Search";
    [Parameter] public string AriaLabel { get; set; } = "Search";
    [Parameter] public Variant Variant { get; set; } = Variant.Outlined;
    [Parameter] public int DebounceMilliseconds { get; set; } = 450;
    [Parameter] public int MinimumLength { get; set; } = 2;
    [Parameter] public bool Clearable { get; set; } = true;
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public bool IsLoading { get; set; }

    private async Task HandleValueChangedAsync(string value)
    {
        Value = value;
        await ValueChanged.InvokeAsync(value);
    }

    private async Task HandleDebounceAsync(string query)
    {
        query = query.Trim();
        if (query.Length != 0 && query.Length < MinimumLength)
        {
            return;
        }

        await OnSearch.InvokeAsync(query);
    }
}
