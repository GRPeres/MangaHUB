namespace MangaHub.Web.API.Services;

public sealed class AdminApiService(ApiHttpClient api)
{
    public async Task<List<UserAdminResponse>> GetUsersAsync() =>
        await api.GetAsync<List<UserAdminResponse>>("/api/admin/users") ?? [];

    public async Task<UserAdminResponse?> UpdateUserRoleAsync(Guid userId, string role) =>
        await api.SendAsync<UpdateUserRoleRequest, UserAdminResponse>(HttpMethod.Put, $"/api/admin/users/{userId}/role", new(role));
}
