using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Cards;

public partial class MangaCardActions
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
