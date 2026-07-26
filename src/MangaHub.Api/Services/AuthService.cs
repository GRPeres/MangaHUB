using MangaHub.Api.Common;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;

namespace MangaHub.Api.Services;

public sealed class AuthService(UserRepository users, IPasswordHasher passwordHasher, ISessionTokenService tokens)
{
    public async Task<UserResponse?> RegisterAsync(AuthRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        if (await users.UsernameExistsAsync(username, cancellationToken))
        {
            return null;
        }

        var isFirstUser = !await users.AnyAsync(cancellationToken);
        var user = new MangaUser
        {
            Username = username,
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = isFirstUser ? "admin" : "user"
        };

        await users.AddAsync(user, cancellationToken);
        var token = tokens.CreateToken(user.Id, user.Username);
        return ApiMapping.ToUserResponse(user, token);
    }

    public async Task<UserResponse?> LoginAsync(AuthRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var user = await users.GetByUsernameAsync(username, cancellationToken);
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return null;
        }

        var token = tokens.CreateToken(user.Id, user.Username);
        return ApiMapping.ToUserResponse(user, token);
    }

    public async Task<UserResponse> UpdatePreferredLanguageAsync(MangaUser user, UpdatePreferredLanguageRequest request, CancellationToken cancellationToken)
    {
        user.PreferredLanguage = NormalizeLanguage(request.PreferredLanguage);
        await users.SaveChangesAsync(cancellationToken);
        return ApiMapping.ToUserResponse(user);
    }

    private static string NormalizeLanguage(string? language)
    {
        var normalized = (language ?? "").Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) ? "en" : normalized[..Math.Min(normalized.Length, 16)];
    }
}
