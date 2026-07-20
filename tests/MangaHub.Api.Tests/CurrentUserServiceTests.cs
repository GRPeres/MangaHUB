using MangaHub.Api.Services;
using MangaHub.Core.Models;
using Microsoft.AspNetCore.Http;

namespace MangaHub.Api.Tests;

public sealed class CurrentUserServiceTests
{
    [Fact]
    public async Task GetCurrentUserAsync_AuthenticatedRequestRecordsSiteActivity()
    {
        await using var db = TestDb.Create();
        var user = new MangaUser { Username = "delta", PasswordHash = "hash" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var tokens = new FakeSessionTokenService();
        var request = new DefaultHttpContext().Request;
        request.Headers.Authorization = $"Bearer {tokens.CreateToken(user.Id, user.Username)}";

        var currentUser = await new CurrentUserService(db, tokens).GetCurrentUserAsync(request, CancellationToken.None);

        Assert.Equal(user.Id, currentUser?.Id);
        var activity = await db.SiteActivities.FindAsync(SiteActivity.SingletonId);
        Assert.NotNull(activity);
        Assert.True(activity.LastActivityAt <= DateTimeOffset.UtcNow);
    }
}
