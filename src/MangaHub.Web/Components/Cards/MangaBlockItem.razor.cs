using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Cards;

public partial class MangaBlockItem
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public MangaBlockSize Size { get; set; } = MangaBlockSize.Small;
    [Parameter] public int? Columns { get; set; }
    [Parameter] public int? Rows { get; set; }
    [Parameter] public string Accent { get; set; } = "";
    [Parameter] public string Class { get; set; } = "";

    private string ItemClass => $"mh-block-item {SizeClass} {Class}";
    private string ItemStyle
    {
        get
        {
            var columns = Columns is null ? "" : $" --mh-block-col-span: {Math.Clamp(Columns.Value, 1, 12)};";
            var rows = Rows is null ? "" : $" --mh-block-row-span: {Math.Clamp(Rows.Value, 1, 6)};";
            var accent = string.IsNullOrWhiteSpace(Accent) ? "" : $" --mh-block-accent: {Accent}; --mh-user-accent: {Accent};";
            return $"{columns}{rows}{accent}".Trim();
        }
    }

    private string SizeClass => Size switch
    {
        MangaBlockSize.Hero => "mh-lego-hero",
        MangaBlockSize.Feature => "mh-lego-feature",
        MangaBlockSize.Tall => "mh-lego-tall",
        MangaBlockSize.Wide => "mh-lego-wide",
        _ => "mh-lego-small"
    };
}
