namespace MangaHub.Web.Services;

using Microsoft.JSInterop;

public sealed class AuthState(MangaHubApiClient api, IJSRuntime js)
{
    private const string StorageKey = "mangahub_session";
    private UserResponse? currentUser;
    private bool loaded;

    public event Action? Changed;

    public UserResponse? CurrentUser => currentUser;
    public bool IsAdmin => string.Equals(currentUser?.Role, "admin", StringComparison.OrdinalIgnoreCase);

    public async Task<UserResponse?> GetCurrentUserAsync(bool forceRefresh = false)
    {
        if (!loaded || forceRefresh)
        {
            var token = await ReadStoredTokenAsync();
            api.SetSessionToken(token);
            currentUser = await api.MeAsync();
            loaded = true;
            Changed?.Invoke();
        }

        return currentUser;
    }

    public async Task<UserResponse?> LoginAsync(string username, string password)
    {
        currentUser = await api.LoginAsync(username, password);
        await StoreTokenAsync(currentUser?.SessionToken ?? "");
        loaded = true;
        Changed?.Invoke();
        return currentUser;
    }

    public async Task<UserResponse?> RegisterAsync(string username, string password)
    {
        currentUser = await api.RegisterAsync(username, password);
        await StoreTokenAsync(currentUser?.SessionToken ?? "");
        loaded = true;
        Changed?.Invoke();
        return currentUser;
    }

    public async Task LogoutAsync()
    {
        await api.LogoutAsync();
        await StoreTokenAsync("");
        currentUser = null;
        loaded = true;
        Changed?.Invoke();
    }

    private async Task<string> ReadStoredTokenAsync()
    {
        try
        {
            return await js.InvokeAsync<string>("localStorage.getItem", StorageKey) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private async Task StoreTokenAsync(string token)
    {
        api.SetSessionToken(token);
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                await js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
            }
            else
            {
                await js.InvokeVoidAsync("localStorage.setItem", StorageKey, token);
            }
        }
        catch
        {
        }
    }
}
