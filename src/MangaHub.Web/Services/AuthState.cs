namespace MangaHub.Web.Services;

public sealed class AuthState(MangaHubApiClient api)
{
    private UserResponse? currentUser;
    private bool loaded;

    public event Action? Changed;

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
        loaded = true;
        Changed?.Invoke();
        return currentUser;
    }

    public async Task<UserResponse?> RegisterAsync(string username, string password)
    {
        currentUser = await api.RegisterAsync(username, password);
        loaded = true;
        Changed?.Invoke();
        return currentUser;
    }

    public async Task LogoutAsync()
    {
        await api.LogoutAsync();
        currentUser = null;
        loaded = true;
        Changed?.Invoke();
    }
}
