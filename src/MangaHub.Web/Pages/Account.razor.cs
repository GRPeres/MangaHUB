using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using MangaHub.Web.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Pages;

public partial class Account
{
    [Inject] private AuthSessionService Auth { get; set; } = default!;
    [Inject] private AdminApiService AdminApi { get; set; } = default!;

    private UserResponse? currentUser;
    private List<UserAdminResponse> users = [];
    private string message = "";
    private Severity messageSeverity = Severity.Info;

    private bool IsAdmin => string.Equals(currentUser?.Role, "admin", StringComparison.OrdinalIgnoreCase);
    private string AccountSummary => IsAdmin
        ? "You can manage your shelf and curate shared catalog access."
        : "You can manage your shelf, reading state, notes, and scores.";
    private string RoleIcon => IsAdmin ? Icons.Material.Filled.AdminPanelSettings : Icons.Material.Filled.Person;
    private string RoleTone => IsAdmin ? "success" : "source";
    private string UserCountLabel => IsAdmin ? $"{users.Count} visible" : "Admin only";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await RefreshAccount();
        await InvokeAsync(StateHasChanged);
    }

    private void OpenLogin() => Auth.RequestLogin("Log in to manage your account.");

    private async Task RefreshAccount()
    {
        currentUser = await Auth.GetCurrentUserAsync(forceRefresh: true);
        if (IsAdmin)
        {
            await LoadUsers();
        }
    }

    private async Task LoadUsers()
    {
        if (!IsAdmin)
        {
            return;
        }

        users = await AdminApi.GetUsersAsync();
    }

    private async Task SetRole(UserAdminResponse user, string role)
    {
        var updated = await AdminApi.UpdateUserRoleAsync(user.Id, role);
        messageSeverity = updated is null ? Severity.Error : Severity.Success;
        message = updated is null ? "Could not update user role." : $"{updated.Username} is now {updated.Role}.";
        await RefreshAccount();
    }

    private async Task MakeAdmin(UserAdminResponse user) => await SetRole(user, "admin");

    private async Task MakeUser(UserAdminResponse user) => await SetRole(user, "user");

    private async Task Logout()
    {
        await Auth.LogoutAsync();
        currentUser = null;
        users = [];
    }

    private bool IsSelf(UserAdminResponse user) => currentUser?.Id == user.Id;

    private static bool IsUserAdmin(UserAdminResponse user) =>
        string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase);

    private static string UserRoleIcon(UserAdminResponse user) =>
        IsUserAdmin(user) ? Icons.Material.Filled.AdminPanelSettings : Icons.Material.Filled.Person;

    private string UserBlockClass(UserAdminResponse user)
    {
        var roleClass = IsUserAdmin(user) ? "mh-user-admin" : "mh-user-standard";
        var selfClass = IsSelf(user) ? "mh-user-self" : "";
        return $"mh-user-block {roleClass} {selfClass}";
    }
}
