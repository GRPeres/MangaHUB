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

        var result = await service.UpdateRoleAsync(Guid.NewGuid(), admin!.Id, new UpdateUserRoleRequest("user"), CancellationToken.None);

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

        var result = await service.UpdateRoleAsync(Guid.NewGuid(), user!.Id, new UpdateUserRoleRequest(" ADMIN "), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("admin", result.User!.Role);
    }

    [Fact]
    public async Task UpdateRoleAsync_PreventsAnAdminFromChangingTheirOwnRole()
    {
        await using var db = TestDb.Create();
        var users = new UserRepository(db);
        var auth = new AuthService(users, new FakePasswordHasher(), new FakeSessionTokenService());
        var admin = await auth.RegisterAsync(new AuthRequest("delta", "secret"), CancellationToken.None);
        var service = new AdminService(users);

        var result = await service.UpdateRoleAsync(admin!.Id, admin.Id, new UpdateUserRoleRequest("user"), CancellationToken.None);

        Assert.Equal("self_role_change", result.Error);
        Assert.Equal("admin", (await users.GetByIdAsync(admin.Id, CancellationToken.None))!.Role);
    }

    [Fact]
    public async Task DeleteTestAccountAsync_DeletesOnlyTemporaryTestAccounts()
    {
        await using var db = TestDb.Create();
        var users = new UserRepository(db);
        var auth = new AuthService(users, new FakePasswordHasher(), new FakeSessionTokenService());
        var normalUser = await auth.RegisterAsync(new AuthRequest("delta", "secret"), CancellationToken.None);
        var testUser = new MangaHub.Core.Models.MangaUser
        {
            Username = "test-account",
            PasswordHash = "hash",
            Role = "test",
            TemporaryAccountCreatedByUserId = normalUser!.Id,
            TemporaryAccountExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
        };
        await users.AddAsync(testUser, CancellationToken.None);
        var service = new AdminService(users);

        Assert.True(await service.DeleteTestAccountAsync(testUser.Id, CancellationToken.None));
        Assert.Null(await users.GetByIdAsync(testUser.Id, CancellationToken.None));
        Assert.False(await service.DeleteTestAccountAsync(normalUser.Id, CancellationToken.None));
        Assert.NotNull(await users.GetByIdAsync(normalUser.Id, CancellationToken.None));
    }
}
