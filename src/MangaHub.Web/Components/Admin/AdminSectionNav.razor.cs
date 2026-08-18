using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Components.Admin;

public partial class AdminSectionNav
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private string CurrentRoute => Navigation.ToBaseRelativePath(Navigation.Uri).Trim('/');
    private static readonly SectionNavigationItem[] Sections =
    [
        new("catalog", "Catalog", Icons.Material.Filled.Inventory2),
        new("operations", "Operations", Icons.Material.Filled.SettingsSuggest)
    ];

    private string ActiveSection => CurrentRoute is "operations" or "admin/operations" ? "operations" : "catalog";

    private Task SelectSection(string section)
    {
        Navigation.NavigateTo($"admin/{section}");
        return Task.CompletedTask;
    }
}
