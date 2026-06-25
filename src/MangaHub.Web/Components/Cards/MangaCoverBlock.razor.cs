using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Components.Cards;

public partial class MangaCoverBlock
{
    [Parameter] public string CoverUrl { get; set; } = "";
    [Parameter] public string AltText { get; set; } = "Manga cover";
    [Parameter] public string PlaceholderIcon { get; set; } = Icons.Material.Filled.MenuBook;
}
