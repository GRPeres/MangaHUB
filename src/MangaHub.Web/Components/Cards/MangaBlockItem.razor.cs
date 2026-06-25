using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Cards;

public partial class MangaBlockItem
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public MangaBlockSize Size { get; set; } = MangaBlockSize.Small;
    [Parameter] public string Class { get; set; } = "";

    private string ItemClass => $"mh-block-item {SizeClass} {Class}";

    private string SizeClass => Size switch
    {
        MangaBlockSize.Hero => "mh-lego-hero",
        MangaBlockSize.Feature => "mh-lego-feature",
        MangaBlockSize.Tall => "mh-lego-tall",
        MangaBlockSize.Wide => "mh-lego-wide",
        _ => "mh-lego-small"
    };
}
