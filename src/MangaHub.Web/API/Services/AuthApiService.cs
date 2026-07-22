namespace MangaHub.Web.API.Services;

public sealed class AuthApiService(ApiHttpClient api)
{
    public async Task<UserResponse?> RegisterAsync(string username, string password) =>
        await api.SendAsync<AuthRequest, UserResponse>(HttpMethod.Post, "/auth/register", new(username, password));

    public async Task<UserResponse?> LoginAsync(string username, string password) =>
        await api.SendAsync<AuthRequest, UserResponse>(HttpMethod.Post, "/auth/login", new(username, password));

    public async Task LogoutAsync() =>
        await api.SendWithoutResponseAsync(HttpMethod.Post, "/auth/logout", new { });

    public async Task<UserResponse?> MeAsync() =>
        await api.GetAsync<UserResponse>("/auth/me");
}

