using MangaHub.Api.Common;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;

namespace MangaHub.Api.Services;

public sealed class AdminService(UserRepository users)
{
    public async Task<List<UserAdminResponse>> ListUsersAsync(CancellationToken cancellationToken)
    {
        var allUsers = await users.ListAsync(cancellationToken);
        return allUsers.Select(ApiMapping.ToUserAdminResponse).ToList();
    }

    public async Task<UpdateUserRoleResult> UpdateRoleAsync(Guid userId, UpdateUserRoleRequest request, CancellationToken cancellationToken)
    {
        var role = TextRules.NormalizeUserRole(request.Role);
        if (role is null)
        {
            return UpdateUserRoleResult.BadRole();
        }

        var targetUser = await users.GetByIdAsync(userId, cancellationToken);
        if (targetUser is null)
        {
            return UpdateUserRoleResult.NotFound();
        }

        if (string.Equals(targetUser.Role, "admin", StringComparison.OrdinalIgnoreCase) && role == "user")
        {
            var adminCount = await users.CountAdminsAsync(cancellationToken);
            if (adminCount <= 1)
            {
                return UpdateUserRoleResult.LastAdmin();
            }
        }

        targetUser.Role = role;
        await users.SaveChangesAsync(cancellationToken);
        return UpdateUserRoleResult.Success(ApiMapping.ToUserAdminResponse(targetUser));
    }
}

public sealed record UpdateUserRoleResult(UserAdminResponse? User, string? Error)
{
    public static UpdateUserRoleResult Success(UserAdminResponse user) => new(user, null);
    public static UpdateUserRoleResult NotFound() => new(null, "not_found");
    public static UpdateUserRoleResult BadRole() => new(null, "bad_role");
    public static UpdateUserRoleResult LastAdmin() => new(null, "last_admin");
}
