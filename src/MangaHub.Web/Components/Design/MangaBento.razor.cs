using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Design;

public partial class MangaBento
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public int Columns { get; set; } = 4;
    [Parameter] public int Gap { get; set; } = 4;
    [Parameter] public bool AnimationEnabled { get; set; } = true;
    [Parameter] public string Class { get; set; } = "";
    [Parameter] public int? ItemMinHeight { get; set; }
    [Parameter] public int? RowHeight { get; set; }

    private string HostClass => $"mh-bento-host {Class}".Trim();
    private string GridClass => $"mh-bento-grid {Class}".Trim();
    private string HostStyle
    {
        get
        {
            var styles = new List<string>();
            if (ItemMinHeight is not null)
            {
                styles.Add($"--mh-bento-item-min-height:{ItemMinHeight}px");
            }

            if (RowHeight is not null)
            {
                styles.Add($"--mh-bento-row-height:{RowHeight}px");
            }

            return string.Join(";", styles);
        }
    }
}
