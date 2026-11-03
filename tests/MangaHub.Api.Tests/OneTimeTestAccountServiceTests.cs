using MangaHub.Api.Repositories;
using MangaHub.Api.Services;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Tests;

public sealed class OneTimeTestAccountServiceTests
{
    [Fact]
    public async Task CreateAsync_GeneratesOneTimeCredentialsAndLimitsEachIssuerToOneActiveAccount()
    {
        await using var db = TestDb.Create();
        var users = new UserRepository(db);
        var service = new OneTimeTestAccountService(users, new FakePasswordHasher(), Options.Create(new MangaHubOptions()));
        var issuerId = Guid.NewGuid();

        var created = await service.CreateAsync(issuerId, new CreateOneTimeTestAccountRequest("admin"), CancellationToken.None);
        var blocked = await service.CreateAsync(issuerId, new CreateOneTimeTestAccountRequest("user"), CancellationToken.None);

        Assert.Null(created.Error);
        Assert.NotNull(created.Credentials);
        Assert.StartsWith("test-", created.Credentials.Username);
        Assert.StartsWith("Test!", created.Credentials.Password);
        Assert.Equal("admin", created.Credentials.Role);
        var stored = await users.GetByUsernameAsync(created.Credentials.Username, CancellationToken.None);
        Assert.Equal(issuerId, stored!.TemporaryAccountCreatedByUserId);
        Assert.Equal(0, stored.TemporaryAccountLoginCount);
        Assert.Equal("active_account_exists", blocked.Error);
    }

    [Fact]
    public async Task LoginAsync_AllowsATemporaryAccountExactlyOnce()
    {
        await using var db = TestDb.Create();
        var users = new UserRepository(db);
        var hasher = new FakePasswordHasher();
        db.Users.Add(new MangaUser
        {
            Username = "test-once",
            PasswordHash = hasher.Hash("secret"),
            Role = "user",
            TemporaryAccountCreatedByUserId = Guid.NewGuid(),
            TemporaryAccountExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync();
        var auth = new AuthService(users, hasher, new FakeSessionTokenService());

        var first = await auth.LoginAsync(new AuthRequest("test-once", "secret"), CancellationToken.None);
        var second = await auth.LoginAsync(new AuthRequest("test-once", "secret"), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(1, (await users.GetByUsernameAsync("test-once", CancellationToken.None))!.TemporaryAccountLoginCount);
    }
}
