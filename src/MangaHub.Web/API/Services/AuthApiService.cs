namespace MangaHub.Web.API.Services;

public sealed class AuthApiService(ApiHttpClient api)
{
    public async Task<UserResponse?> RegisterAsync(string username, string password, string email) =>
        await api.SendAsync<AuthRequest, UserResponse>(HttpMethod.Post, "/auth/register", new(username, password, email));

    public async Task<UserResponse?> LoginAsync(string username, string password) =>
        await api.SendAsync<AuthRequest, UserResponse>(HttpMethod.Post, "/auth/login", new(username, password));

    public Task<ApiCallResult<object>> RequestPasswordResetAsync(string email) =>
        api.SendWithResultAsync<ForgotPasswordRequest, object>(HttpMethod.Post, "/auth/forgot-password", new(email));

    public Task<ApiCallResult<object>> ResetPasswordAsync(string token, string password) =>
        api.SendWithResultAsync<ResetPasswordRequest, object>(HttpMethod.Post, "/auth/reset-password", new(token, password));

    public Task<ApiCallResult<UserResponse>> UpdateAccountAsync(string email, string currentPassword, string newPassword) =>
        api.SendWithResultAsync<UpdateAccountRequest, UserResponse>(HttpMethod.Put, "/auth/account", new(email, currentPassword, newPassword));

    public async Task LogoutAsync() =>
        await api.SendWithoutResponseAsync(HttpMethod.Post, "/auth/logout", new { });

    public async Task<UserResponse?> MeAsync() =>
        await api.GetAsync<UserResponse>("/auth/me");

    public async Task<UserResponse?> UpdatePreferredLanguageAsync(string language) =>
        await api.SendAsync<UpdatePreferredLanguageRequest, UserResponse>(
            HttpMethod.Put,
            "/auth/preferences",
            new(language));
}

