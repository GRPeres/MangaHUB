using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;

namespace MangaHub.Api.Services;

public sealed class CurrentUserService(MangaHubDbContext db, ISessionTokenService tokens)
{
    public async Task<MangaUser?> GetCurrentUserAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var bearerToken = ReadBearerToken(request);
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            var bearerUserId = tokens.ReadUserId(bearerToken);
            if (bearerUserId is not null)
            {
                return await db.Users.FindAsync([bearerUserId.Value], cancellationToken);
            }
        }

        if (!request.Cookies.TryGetValue("mangahub_session", out var token))
        {
            return null;
        }

        var userId = tokens.ReadUserId(token);
        return userId is null ? null : await db.Users.FindAsync([userId.Value], cancellationToken);
    }

    public static bool IsAdmin(MangaUser user) =>
        string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase);

    private static string ReadBearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..].Trim() : "";
    }
}
