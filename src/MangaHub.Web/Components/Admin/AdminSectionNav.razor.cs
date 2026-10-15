using Microsoft.AspNetCore.Components;
using MangaHub.Web.API.Services;
using MudBlazor;

namespace MangaHub.Web.Components.Admin;

public partial class AdminSectionNav
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private AdminApiService AdminApi { get; set; } = default!;

    private int _openIssueCount;
    private int _catalogCount;
    private bool mobileSectionsExpanded;

    private string CurrentRoute => Navigation.ToBaseRelativePath(Navigation.Uri).Trim('/');
    private IReadOnlyList<SectionNavigationItem> Sections =>
    [
        new("catalog", "Catalog", Icons.Material.Filled.Inventory2, Count: _catalogCount, IsPrimary: true),
        new("issues", "Issues", Icons.Material.Filled.ReportProblem, Color.Error, IsPrimary: true, Count: _openIssueCount),
        new("operations", "Operations", Icons.Material.Filled.SettingsSuggest),
        new("archive", "Archive", Icons.Material.Filled.Archive)
    ];

    private string ActiveSection => CurrentRoute switch
    {
        "operations" or "admin/operations" => "operations",
        "archive" or "admin/archive" => "archive",
        "issues" or "admin/issues" => "issues",
        _ => "catalog"
    };

    protected override async Task OnInitializedAsync()
    {
        mobileSectionsExpanded = !IsPrimarySection(ActiveSection);
        var catalogCount = AdminApi.GetCatalogCountAsync();
        var issueCount = AdminApi.GetOpenIssueCountAsync();
        await Task.WhenAll(catalogCount, issueCount);
        _catalogCount = await catalogCount;
        _openIssueCount = await issueCount;
    }

    private Task SelectSection(string section)
    {
        Navigation.NavigateTo($"admin/{section}");
        return Task.CompletedTask;
    }

    private Task OnMobileSectionsExpandedChanged(bool expanded)
    {
        mobileSectionsExpanded = expanded;
        return Task.CompletedTask;
    }

    private static bool IsPrimarySection(string section) => section is "catalog" or "issues";
}
