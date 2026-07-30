using MangaHub.Web.API.DTOs;
using MangaHub.Web.Services;
using MangaHub.Web.API.Services;
using MangaHub.Web.API.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace MangaHub.Web.Pages.Layout;

public partial class MainLayout : IDisposable
{
    [Inject] private AuthSessionService Auth { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ThemePreferenceService ThemePreference { get; set; } = default!;
    [Inject] private NotificationApiService Notifications { get; set; } = default!;
    [Inject] private Microsoft.JSInterop.IJSRuntime JS { get; set; } = default!;

    private bool _drawerExpanded;
    private bool _darkMode;
    private UserResponse? _currentUser;
    private bool _loginOpen;
    private string _loginMessage = "Please log in to continue.";
    private string? _pendingRoute;
    private List<MangaNotificationResponse> _notifications = [];
    private int _unreadNotificationCount;
    private bool IsAdmin => string.Equals(_currentUser?.Role, "admin", StringComparison.OrdinalIgnoreCase);
    private bool IsLocalhost => Navigation.Uri.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
        || Navigation.Uri.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase)
        || Navigation.Uri.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || Navigation.Uri.StartsWith("https://127.0.0.1", StringComparison.OrdinalIgnoreCase);
    private string DrawerClass => _drawerExpanded ? "mh-drawer mh-drawer-expanded" : "mh-drawer mh-drawer-compact";

    protected override async Task OnInitializedAsync()
    {
        Auth.Changed += OnAuthChanged;
        Auth.LoginRequested += OnLoginRequested;
        Navigation.LocationChanged += OnLocationChanged;
        _darkMode = await ThemePreference.GetDarkModeAsync() ?? false;
        _currentUser = await Auth.GetCurrentUserAsync();
        await LoadNotificationsAsync();
        GuardCurrentRoute();
    }

    private void ToggleDrawer() => _drawerExpanded = !_drawerExpanded;
    private async Task ToggleTheme()
    {
        _darkMode = !_darkMode;
        await ThemePreference.SetDarkModeAsync(_darkMode);
    }

    private void GoHome() => Navigation.NavigateTo("");
    private void GoLibrary() => Navigate("library");
    private void GoAccount() => Navigate("account");
    private void GoBentoCardLab() => Navigate("bento-card-lab");

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

    private bool RequiresLogin(string route)
    {
        var cleanRoute = route.Trim('/').Split('?', '#')[0];
        if (cleanRoute is "bento-card-lab")
        {
            return !IsLocalhost;
        }

        return cleanRoute is "library" or "search" or "catalog" or "account" or "bento-card-lab";
    }

    private bool IsActive(string route)
    {
        var relative = Navigation.ToBaseRelativePath(Navigation.Uri).TrimEnd('/');
        return string.IsNullOrWhiteSpace(route)
            ? string.IsNullOrWhiteSpace(relative)
            : string.Equals(relative, route, StringComparison.OrdinalIgnoreCase);
    }

    private string MobileNavClass(string route) =>
        IsActive(route) ? "mh-mobile-nav-item mh-mobile-nav-item-active" : "mh-mobile-nav-item";

    private async Task Logout()
    {
        await Auth.LogoutAsync();
        Navigation.NavigateTo("");
    }

    private void OnAuthChanged()
    {
        _currentUser = Auth.CurrentUser;
        _ = LoadNotificationsAsync();
        _ = InvokeAsync(StateHasChanged);
    }

    private async Task LoadNotificationsAsync()
    {
        if (_currentUser is null)
        {
            _notifications = [];
            _unreadNotificationCount = 0;
            return;
        }

        try
        {
            _notifications = await Notifications.GetAsync() ?? [];
            _unreadNotificationCount = _notifications.Count(notification => notification.ReadAt is null);
            await InvokeAsync(StateHasChanged);
        }
        catch (HttpRequestException)
        {
            _notifications = [];
            _unreadNotificationCount = 0;
        }
    }

    private async Task OpenNotification(MangaNotificationResponse notification)
    {
        if (notification.ReadAt is null)
        {
            await Notifications.MarkReadAsync(notification.Id);
        }
        Navigation.NavigateTo("library");
    }

    private async Task EnablePhoneNotifications()
    {
        var publicKey = await Notifications.GetPushPublicKeyAsync();
        if (string.IsNullOrWhiteSpace(publicKey)) return;
        var subscription = await JS.InvokeAsync<WebPushSubscriptionRequest?>("mangaHubPush.subscribe", new object?[] { publicKey });
        if (subscription is not null) await Notifications.SubscribeToPushAsync(subscription);
    }

    public void Dispose()
    {
        Auth.Changed -= OnAuthChanged;
        Auth.LoginRequested -= OnLoginRequested;
        Navigation.LocationChanged -= OnLocationChanged;
    }
}
