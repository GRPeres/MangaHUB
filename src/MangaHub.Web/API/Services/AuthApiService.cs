namespace MangaHub.Web.API.Services;

using MangaHub.Web.Services;

public sealed class AuthApiService(ApiHttpClient api, SessionTokenStore tokens)
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
            currentUser = await MeAsync();
            loaded = true;
            Changed?.Invoke();
        }

        return currentUser;
    }

    public async Task<UserResponse?> RegisterAsync(string username, string password)
    {
        currentUser = await SendRegisterAsync(username, password);
        await tokens.SetAsync(currentUser?.SessionToken ?? "");
        loaded = true;
        Changed?.Invoke();
        return currentUser;
    }

    public async Task<UserResponse?> LoginAsync(string username, string password)
    {
        currentUser = await SendLoginAsync(username, password);
        await tokens.SetAsync(currentUser?.SessionToken ?? "");
        loaded = true;
        Changed?.Invoke();
        return currentUser;
    }

    public async Task LogoutAsync()
    {
        await SendLogoutAsync();
        await tokens.SetAsync("");
        currentUser = null;
        loaded = true;
        Changed?.Invoke();
    }

    public void RequestLogin(string message = "Please log in to continue.", string? returnUrl = null)
    {
        LoginRequested?.Invoke(new LoginPrompt(message, returnUrl));
    }

    private async Task<UserResponse?> SendRegisterAsync(string username, string password) =>
        await api.SendAsync<AuthRequest, UserResponse>(HttpMethod.Post, "/auth/register", new(username, password));

    private async Task<UserResponse?> SendLoginAsync(string username, string password) =>
        await api.SendAsync<AuthRequest, UserResponse>(HttpMethod.Post, "/auth/login", new(username, password));

    private async Task SendLogoutAsync() =>
        await api.SendAsync<object, object>(HttpMethod.Post, "/auth/logout", new { });

    private async Task<UserResponse?> MeAsync() =>
        await api.GetAsync<UserResponse>("/auth/me");
}

