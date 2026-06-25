using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Bento;

public partial class BentoBlock
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public string Variant { get; set; } = "standard";

    private string BlockClass => $"mh-bento-block mh-bento-block-{Variant} {Class}";
}
