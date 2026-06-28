using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Design;

public partial class MangaBento
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public int Columns { get; set; } = 4;
    [Parameter] public int Gap { get; set; } = 4;
    [Parameter] public bool AnimationEnabled { get; set; } = true;
    [Parameter] public string Class { get; set; } = "";

    private string GridClass => $"mh-bento-grid {Class}".Trim();
}
