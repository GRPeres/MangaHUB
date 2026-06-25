using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Cards;

public partial class MangaSummaryBlock
{
    [Parameter, EditorRequired] public string Title { get; set; } = "";
    [Parameter] public string Subtitle { get; set; } = "";
    [Parameter] public string Summary { get; set; } = "";
    [Parameter] public string Notes { get; set; } = "";
}
