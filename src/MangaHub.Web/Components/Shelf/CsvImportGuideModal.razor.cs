using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MangaHub.Web.Components.Shelf;

public partial class CsvImportGuideModal
{
    private const string GuidePreferenceKey = "mangahub_csv_import_guide_hidden";

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Parameter] public bool Open { get; set; }
    [Parameter] public bool CanCreateCatalog { get; set; }

    private bool guideVisible;
    private bool hideGuide;
    private bool checkedPreference;

    protected override void OnParametersSet()
    {
        if (!Open)
        {
            guideVisible = false;
            hideGuide = false;
            checkedPreference = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!Open || checkedPreference)
        {
            return;
        }

        checkedPreference = true;
        guideVisible = !await JS.InvokeAsync<bool>("mangaHubStorage.getBoolean", GuidePreferenceKey);
        await InvokeAsync(StateHasChanged);
    }

    private async Task Continue()
    {
        if (hideGuide)
        {
            await JS.InvokeVoidAsync("mangaHubStorage.setBoolean", GuidePreferenceKey, true);
        }

        guideVisible = false;
    }
}
