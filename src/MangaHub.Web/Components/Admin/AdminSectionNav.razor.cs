using Microsoft.AspNetCore.Components;
using MangaHub.Web.API.Services;
using MudBlazor;

namespace MangaHub.Web.Components.Admin;

public partial class AdminSectionNav
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private AdminApiService AdminApi { get; set; } = default!;

    private int _openIssueCount;

    private string CurrentRoute => Navigation.ToBaseRelativePath(Navigation.Uri).Trim('/');
    private IReadOnlyList<SectionNavigationItem> Sections =>
    [
        new("catalog", "Catalog", Icons.Material.Filled.Inventory2),
        new("issues", "Issues", Icons.Material.Filled.ReportProblem, Color.Error, Count: _openIssueCount),
        new("operations", "Operations", Icons.Material.Filled.SettingsSuggest)
    ];

    private string ActiveSection => CurrentRoute switch
    {
        "operations" or "admin/operations" => "operations",
        "issues" or "admin/issues" => "issues",
        _ => "catalog"
    };

    protected override async Task OnInitializedAsync() =>
        _openIssueCount = await AdminApi.GetOpenIssueCountAsync();

    private Task SelectSection(string section)
    {
        Navigation.NavigateTo($"admin/{section}");
        return Task.CompletedTask;
    }
}
