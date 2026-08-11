using Microsoft.AspNetCore.Components;

namespace MangaHub.Web.Components.Admin;

public partial class AdminSectionNav
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private string CurrentRoute => Navigation.ToBaseRelativePath(Navigation.Uri).Trim('/');
    private bool IsCatalogActive => CurrentRoute is "catalog" or "admin/catalog";
    private bool IsOperationsActive => CurrentRoute is "operations" or "admin/operations";

    private void GoCatalog() => Navigation.NavigateTo("admin/catalog");
    private void GoOperations() => Navigation.NavigateTo("admin/operations");
}
