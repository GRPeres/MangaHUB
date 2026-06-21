namespace MangaHub.Web.Services;

public sealed class AuthState(AuthApiService api, SessionTokenStore tokens)
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
            currentUser = await api.MeAsync();
            loaded = true;
            Changed?.Invoke();
        }

        return currentUser;
    }

    public async Task<UserResponse?> LoginAsync(string username, string password)
    {
        currentUser = await api.LoginAsync(username, password);
        await tokens.SetAsync(currentUser?.SessionToken ?? "");
        loaded = true;
        Changed?.Invoke();
        return currentUser;
    }

    public async Task<UserResponse?> RegisterAsync(string username, string password)
    {
        currentUser = await api.RegisterAsync(username, password);
        await tokens.SetAsync(currentUser?.SessionToken ?? "");
        loaded = true;
        Changed?.Invoke();
        return currentUser;
    }

    public async Task LogoutAsync()
    {
        await api.LogoutAsync();
        await tokens.SetAsync("");
        currentUser = null;
        loaded = true;
        Changed?.Invoke();
    }

    public void RequestLogin(string message = "Please log in to continue.", string? returnUrl = null)
    {
        LoginRequested?.Invoke(new LoginPrompt(message, returnUrl));
    }
}

public sealed record LoginPrompt(string Message, string? ReturnUrl);
