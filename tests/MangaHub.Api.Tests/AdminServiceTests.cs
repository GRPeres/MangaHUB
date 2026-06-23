using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;

namespace MangaHub.Api.Tests;

public sealed class AdminServiceTests
{
    [Fact]
    public async Task UpdateRoleAsync_PreventsDemotingLastAdmin()
    {
        await using var db = TestDb.Create();
        var auth = new AuthService(new UserRepository(db), new FakePasswordHasher(), new FakeSessionTokenService());
        var admin = await auth.RegisterAsync(new AuthRequest("delta", "secret"), CancellationToken.None);
        var service = new AdminService(new UserRepository(db));

        var result = await service.UpdateRoleAsync(admin!.Id, new UpdateUserRoleRequest("user"), CancellationToken.None);

        Assert.Equal("last_admin", result.Error);
    }

    [Fact]
    public async Task UpdateRoleAsync_AllowsPromotingUserAndNormalizesRole()
    {
        await using var db = TestDb.Create();
        var users = new UserRepository(db);
        var auth = new AuthService(users, new FakePasswordHasher(), new FakeSessionTokenService());
        await auth.RegisterAsync(new AuthRequest("delta", "secret"), CancellationToken.None);
        var user = await auth.RegisterAsync(new AuthRequest("beta", "secret"), CancellationToken.None);
        var service = new AdminService(users);

        var result = await service.UpdateRoleAsync(user!.Id, new UpdateUserRoleRequest(" ADMIN "), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("admin", result.User!.Role);
    }
}
