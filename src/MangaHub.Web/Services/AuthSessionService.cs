namespace MangaHub.Web.Services;

using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;

public sealed class AuthSessionService(AuthApiService authApi, SessionTokenStore tokens)
{
    private UserResponse? currentUser;
    private bool loaded;

    public event Action? Changed;
    public event Action<LoginPrompt>? LoginRequested;

    public UserResponse? CurrentUser => currentUser;
    public bool IsAdmin => string.Equals(currentUser?.Role, "admin", StringComparison.OrdinalIgnoreCase);

    public async Task<UserResponse?> GetCurrentUserAsync(bool forceRefresh = false)
    {
        if (!loaded || forceRefresh)
        {
            currentUser = await authApi.MeAsync();
            loaded = true;
            Changed?.Invoke();
        }

        return currentUser;
    }

    public async Task<UserResponse?> RegisterAsync(string username, string password)
    {
        currentUser = await authApi.RegisterAsync(username, password);
        await tokens.SetAsync(currentUser?.SessionToken ?? "");
        loaded = true;
        Changed?.Invoke();
        return currentUser;
    }

    public async Task<UserResponse?> LoginAsync(string username, string password)
    {
        currentUser = await authApi.LoginAsync(username, password);
        await tokens.SetAsync(currentUser?.SessionToken ?? "");
        loaded = true;
        Changed?.Invoke();
        return currentUser;
    }

    public async Task LogoutAsync()
    {
        await authApi.LogoutAsync();
        await tokens.SetAsync("");
        currentUser = null;
        loaded = true;
        Changed?.Invoke();
    }

    public async Task<UserResponse?> UpdatePreferredLanguageAsync(string language)
    {
        var updated = await authApi.UpdatePreferredLanguageAsync(language);
        if (updated is not null)
        {
            currentUser = updated with { SessionToken = currentUser?.SessionToken ?? "" };
            loaded = true;
            Changed?.Invoke();
        }

        return currentUser;
    }

    public void RequestLogin(string message = "Please log in to continue.", string? returnUrl = null)
    {
        LoginRequested?.Invoke(new LoginPrompt(message, returnUrl));
    }
}
