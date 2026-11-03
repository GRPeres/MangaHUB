using System.Security.Cryptography;
using MangaHub.Api.Common;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Services;

public sealed class OneTimeTestAccountService(
    UserRepository users,
    IPasswordHasher passwordHasher,
    IOptions<MangaHubOptions> options)
{
    public async Task<OneTimeTestAccountResult> CreateAsync(Guid issuingAdminUserId, CreateOneTimeTestAccountRequest request, CancellationToken cancellationToken)
    {
        var role = TextRules.NormalizeUserRole(request.Role);
        if (role is null)
        {
            return OneTimeTestAccountResult.BadRole();
        }

        var now = DateTimeOffset.UtcNow;
        if (await users.HasUnclaimedTemporaryAccountAsync(issuingAdminUserId, now, cancellationToken))
        {
            return OneTimeTestAccountResult.ActiveAccountExists();
        }

        var username = await CreateUsernameAsync(cancellationToken);
        var password = $"Test!{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}";
        var expiresAt = now.AddMinutes(Math.Clamp(options.Value.OneTimeTestAccountClaimMinutes, 5, 60));
        var account = new MangaUser
        {
            Username = username,
            PasswordHash = passwordHasher.Hash(password),
            Role = role,
            TemporaryAccountCreatedByUserId = issuingAdminUserId,
            TemporaryAccountExpiresAt = expiresAt
        };

        await users.AddAsync(account, cancellationToken);
        return OneTimeTestAccountResult.Success(new OneTimeTestAccountCredentialsResponse(username, password, role, expiresAt));
    }

    private async Task<string> CreateUsernameAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var username = $"test-{Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant()}";
            if (!await users.UsernameExistsAsync(username, cancellationToken))
            {
                return username;
            }
        }

        throw new InvalidOperationException("Could not allocate a one-time test account username.");
    }
}

public sealed record OneTimeTestAccountResult(OneTimeTestAccountCredentialsResponse? Credentials, string? Error)
{
    public static OneTimeTestAccountResult Success(OneTimeTestAccountCredentialsResponse credentials) => new(credentials, null);
    public static OneTimeTestAccountResult BadRole() => new(null, "bad_role");
    public static OneTimeTestAccountResult ActiveAccountExists() => new(null, "active_account_exists");
}
