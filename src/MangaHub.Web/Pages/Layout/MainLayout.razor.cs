using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using MangaHub.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace MangaHub.Web.Pages.Layout;

public partial class MainLayout : IDisposable
{
    [Inject] private AuthState Auth { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private bool _drawerExpanded;
    private bool _darkMode;
    private UserResponse? _currentUser;
    private bool _loginOpen;
    private string _loginMessage = "Please log in to continue.";
    private string? _pendingRoute;
    private string DrawerClass => _drawerExpanded ? "mh-drawer mh-drawer-expanded" : "mh-drawer mh-drawer-compact";

    protected override async Task OnInitializedAsync()
    {
        Auth.Changed += OnAuthChanged;
        Auth.LoginRequested += OnLoginRequested;
        Navigation.LocationChanged += OnLocationChanged;
        _currentUser = await Auth.GetCurrentUserAsync();
        GuardCurrentRoute();
    }

    private void ToggleDrawer() => _drawerExpanded = !_drawerExpanded;
    private void ToggleTheme() => _darkMode = !_darkMode;

    private void GoHome() => Navigation.NavigateTo("");
    private void GoLibrary() => Navigate("library");
    private void GoSearch() => Navigate("search");
    private void GoAccount() => Navigate("account");

    private void Navigate(string route)
    {
        if (RequiresLogin(route) && _currentUser is null)
        {
            RequestLoginForRoute(route);
            return;
        }

        Navigation.NavigateTo(route);
    }

    private void OpenLogin()
    {
        _pendingRoute = null;
        _loginMessage = "Log in to manage your shelf and account.";
        _loginOpen = true;
    }

    private void RequestLoginForRoute(string route)
    {
        _pendingRoute = route;
        _loginMessage = "Please log in to continue.";
        _loginOpen = true;
    }

    private void OnLoginRequested(LoginPrompt prompt)
    {
        _pendingRoute = prompt.ReturnUrl;
        _loginMessage = string.IsNullOrWhiteSpace(prompt.Message) ? "Please log in to continue." : prompt.Message;
        _loginOpen = true;
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnLoginAuthenticated(UserResponse user)
    {
        _currentUser = user;
        var route = _pendingRoute;
        _pendingRoute = null;
        if (!string.IsNullOrWhiteSpace(route))
        {
            var currentRoute = Navigation.ToBaseRelativePath(Navigation.Uri).TrimEnd('/');
            Navigation.NavigateTo(route, forceLoad: string.Equals(currentRoute, route, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        GuardCurrentRoute();
        _ = InvokeAsync(StateHasChanged);
    }

    private void GuardCurrentRoute()
    {
        var route = Navigation.ToBaseRelativePath(Navigation.Uri).TrimEnd('/');
        if (RequiresLogin(route) && _currentUser is null)
        {
            RequestLoginForRoute(route);
        }
    }

    private static bool RequiresLogin(string route)
    {
        var cleanRoute = route.Trim('/').Split('?', '#')[0];
        return cleanRoute is "library" or "search" or "account";
    }

    private bool IsActive(string route)
    {
        var relative = Navigation.ToBaseRelativePath(Navigation.Uri).TrimEnd('/');
        return string.IsNullOrWhiteSpace(route)
            ? string.IsNullOrWhiteSpace(relative)
            : string.Equals(relative, route, StringComparison.OrdinalIgnoreCase);
    }

    private async Task Logout()
    {
        await Auth.LogoutAsync();
        Navigation.NavigateTo("");
    }

    private void OnAuthChanged()
    {
        _currentUser = Auth.CurrentUser;
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        Auth.Changed -= OnAuthChanged;
        Auth.LoginRequested -= OnLoginRequested;
        Navigation.LocationChanged -= OnLocationChanged;
    }
}
