using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Bento;

public partial class BentoItem
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public BentoBlockSize Size { get; set; } = BentoBlockSize.Small;
    [Parameter] public int? Width { get; set; }
    [Parameter] public int? Height { get; set; }
    [Parameter] public int? Columns { get; set; }
    [Parameter] public int? Rows { get; set; }
    [Parameter] public string Accent { get; set; } = "";
    [Parameter] public string Class { get; set; } = "";

    private string ItemClass => $"mh-bento-item {Class}";

    private string ItemStyle
    {
        get
        {
            var width = Math.Clamp(Columns ?? Width ?? DefaultWidth, 1, 12);
            var height = Math.Clamp(Rows ?? Height ?? DefaultHeight, 1, 6);
            var accent = string.IsNullOrWhiteSpace(Accent) ? "" : $" --mh-block-accent: {Accent}; --mh-user-accent: {Accent};";
            return $"--mh-bento-item-width: {width}; --mh-bento-item-height: {height};{accent}";
        }
    }

    private int DefaultWidth => Size switch
    {
        BentoBlockSize.Hero => 6,
        BentoBlockSize.Feature => 6,
        BentoBlockSize.Wide => 6,
        BentoBlockSize.Tall => 3,
        _ => 3
    };

    private int DefaultHeight => Size switch
    {
        BentoBlockSize.Hero => 2,
        BentoBlockSize.Feature => 2,
        BentoBlockSize.Tall => 2,
        _ => 1
    };
}
