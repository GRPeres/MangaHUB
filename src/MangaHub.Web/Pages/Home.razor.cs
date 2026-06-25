using MangaHub.Web.API.DTOs;
using MangaHub.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Pages;

public partial class Home : IDisposable
{
    [Inject] private AuthSessionService Auth { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private UserResponse? currentUser;

    private bool IsAdmin => string.Equals(currentUser?.Role, "admin", StringComparison.OrdinalIgnoreCase);
    private string SessionLabel => currentUser is null ? "Log in" : currentUser.Username;
    private string SessionIcon => currentUser is null ? Icons.Material.Filled.Login : Icons.Material.Filled.AccountCircle;
    private string SessionTone => currentUser is null ? "warning" : IsAdmin ? "success" : "source";
    private string AdminEyebrow => IsAdmin ? "Admin tools" : "Account";
    private string AdminTitle => IsAdmin ? "Curate the shared catalog" : "Sign in to manage your shelf";
    private string AdminCopy => IsAdmin
        ? "Add metadata manually, enrich from MAL, import CSV rows, and keep shared entries clean."
        : "Normal users manage their own shelf while admins maintain shared manga metadata.";
    private string AdminIcon => IsAdmin ? Icons.Material.Filled.AdminPanelSettings : Icons.Material.Filled.ManageAccounts;
    private Color AdminColor => IsAdmin ? Color.Primary : Color.Secondary;

    protected override async Task OnInitializedAsync()
    {
        Auth.Changed += OnAuthChanged;
        currentUser = await Auth.GetCurrentUserAsync();
    }

    private void GoAccountOrLogin()
    {
        if (currentUser is null)
        {
            Auth.RequestLogin("Log in to manage your shelf and account.");
            return;
        }

        Navigation.NavigateTo("account");
    }

    private void GoCatalogOrAccount()
    {
        if (IsAdmin)
        {
            GoCatalog();
            return;
        }

        GoAccountOrLogin();
    }

    private void GoTo(string route)
    {
        if (currentUser is null)
        {
            Auth.RequestLogin("Please log in to continue.", route);
            return;
        }

        Navigation.NavigateTo(route);
    }

    private void GoLibrary() => GoTo("library");

    private void GoCatalog() => GoTo("catalog");

    private void OnAuthChanged()
    {
        currentUser = Auth.CurrentUser;
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        Auth.Changed -= OnAuthChanged;
    }
}
