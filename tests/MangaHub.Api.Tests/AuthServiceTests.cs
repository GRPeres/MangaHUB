using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;

namespace MangaHub.Api.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task RegisterAsync_MakesFirstUserAdminAndTrimsUsername()
    {
        await using var db = TestDb.Create();
        var service = new AuthService(new UserRepository(db), new FakePasswordHasher(), new FakeSessionTokenService());

        var user = await service.RegisterAsync(new AuthRequest(" delta ", "secret"), CancellationToken.None);

        Assert.NotNull(user);
        Assert.Equal("delta", user.Username);
        Assert.Equal("admin", user.Role);
        Assert.StartsWith("token:", user.SessionToken);
    }

    [Fact]
    public async Task RegisterAsync_RejectsDuplicateUsername()
    {
        await using var db = TestDb.Create();
        var service = new AuthService(new UserRepository(db), new FakePasswordHasher(), new FakeSessionTokenService());

        await service.RegisterAsync(new AuthRequest("delta", "secret"), CancellationToken.None);
        var duplicate = await service.RegisterAsync(new AuthRequest("delta", "other"), CancellationToken.None);

        Assert.Null(duplicate);
    }

    [Fact]
    public async Task LoginAsync_ReturnsUserForValidPasswordOnly()
    {
        await using var db = TestDb.Create();
        var service = new AuthService(new UserRepository(db), new FakePasswordHasher(), new FakeSessionTokenService());
        await service.RegisterAsync(new AuthRequest("delta", "secret"), CancellationToken.None);

        var valid = await service.LoginAsync(new AuthRequest(" delta ", "secret"), CancellationToken.None);
        var invalid = await service.LoginAsync(new AuthRequest("delta", "wrong"), CancellationToken.None);

        Assert.NotNull(valid);
        Assert.Null(invalid);
    }

    [Fact]
    public async Task UpdatePreferredLanguageAsync_PersistsNormalizedLanguage()
    {
        await using var db = TestDb.Create();
        var users = new UserRepository(db);
        var service = new AuthService(users, new FakePasswordHasher(), new FakeSessionTokenService());
        var registered = await service.RegisterAsync(new AuthRequest("delta", "secret"), CancellationToken.None);
        var user = await users.GetByIdAsync(registered!.Id, CancellationToken.None);

        var updated = await service.UpdatePreferredLanguageAsync(user!, new UpdatePreferredLanguageRequest(" PT-BR "), CancellationToken.None);

        Assert.Equal("pt-br", updated.PreferredLanguage);
        Assert.Equal("pt-br", (await users.GetByIdAsync(registered.Id, CancellationToken.None))!.PreferredLanguage);
    }
}
