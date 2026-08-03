using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;

namespace MangaHub.Api.Services;

public sealed class CurrentUserService(MangaHubDbContext db, ISessionTokenService tokens)
{
    private static long nextActivityWriteTicks;

    public async Task<MangaUser?> GetCurrentUserAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        // Browser requests include both a persistent session cookie and a cached bearer token.
        // Prefer the cookie so an old token from a previous account cannot override the active session.
        if (request.Cookies.TryGetValue("mangahub_session", out var cookieToken))
        {
            var cookieUserId = tokens.ReadUserId(cookieToken);
            var cookieUser = cookieUserId is null ? null : await db.Users.FindAsync([cookieUserId.Value], cancellationToken);
            if (cookieUser is not null)
            {
                await RecordActivityAsync(cookieUser, cancellationToken);
                return cookieUser;
            }
        }

        var bearerToken = ReadBearerToken(request);
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            var bearerUserId = tokens.ReadUserId(bearerToken);
            if (bearerUserId is not null)
            {
                var bearerUser = await db.Users.FindAsync([bearerUserId.Value], cancellationToken);
                await RecordActivityAsync(bearerUser, cancellationToken);
                return bearerUser;
            }
        }

        return null;
    }

    public static bool IsAdmin(MangaUser user) =>
        string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase);

    private static string ReadBearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..].Trim() : "";
    }

    private async Task RecordActivityAsync(MangaUser? user, CancellationToken cancellationToken)
    {
        if (user is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var nowTicks = now.UtcDateTime.Ticks;
        var nextTicks = Interlocked.Read(ref nextActivityWriteTicks);
        if (nowTicks < nextTicks || Interlocked.CompareExchange(ref nextActivityWriteTicks, nowTicks + TimeSpan.TicksPerMinute, nextTicks) != nextTicks)
        {
            return;
        }

        var activity = await db.SiteActivities.FindAsync([SiteActivity.SingletonId], cancellationToken);
        if (activity is null)
        {
            db.SiteActivities.Add(new SiteActivity { LastActivityAt = now });
        }
        else
        {
            activity.LastActivityAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
