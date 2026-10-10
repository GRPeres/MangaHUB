using MangaHub.Api.Repositories;
using MangaHub.Core.Models;

namespace MangaHub.Api.Tests;

public sealed class DashboardRepositoryTests
{
    [Fact]
    public async Task GetAsync_DoesNotContinueCaughtUpMangaDexTitle()
    {
        await using var db = TestDb.Create();
        var user = new MangaUser { Username = "reader", PasswordHash = "hash" };
        var caughtUp = new MangaEntry { Title = "Caught up", MangaDexId = "c0ffee00-0000-4000-8000-000000000000" };
        var planned = new MangaEntry { Title = "Planned next" };
        db.AddRange(user, caughtUp, planned);
        db.UserMangaEntries.AddRange(
            new UserMangaEntry
            {
                UserId = user.Id,
                MangaEntryId = caughtUp.Id,
                ReadingStatus = "reading",
                CurrentChapter = "37.2",
                IsRead = true
            },
            new UserMangaEntry { UserId = user.Id, MangaEntryId = planned.Id, ReadingStatus = "planned" });
        db.MangaDexLanguageLatestChapters.Add(new MangaDexLanguageLatestChapter
        {
            MangaEntryId = caughtUp.Id,
            Language = "en",
            LatestChapter = 37.2m
        });
        await db.SaveChangesAsync();

        var dashboard = await new DashboardRepository(db).GetAsync(user.Id, "en", DateTimeOffset.UtcNow.AddDays(-7), CancellationToken.None);

        Assert.NotNull(dashboard.ContinueReading);
        Assert.Equal(planned.Id, dashboard.ContinueReading!.Id);
    }

    [Fact]
    public async Task GetAsync_DoesNotContinueRecentlyVerifiedExternalTitleWithoutNewProgress()
    {
        await using var db = TestDb.Create();
        var user = new MangaUser { Username = "reader", PasswordHash = "hash" };
        var external = new MangaEntry { Title = "Checked external", FallbackReaderUrl = "https://reader.example/checked" };
        var planned = new MangaEntry { Title = "Planned next" };
        db.AddRange(user, external, planned);
        db.UserMangaEntries.AddRange(
            new UserMangaEntry
            {
                UserId = user.Id,
                MangaEntryId = external.Id,
                ReadingStatus = "reading",
                CurrentChapter = "38",
                ExternalReaderLatestChapter = "38",
                LastExternalReaderVerifiedAt = DateTimeOffset.UtcNow.AddDays(-1)
            },
            new UserMangaEntry { UserId = user.Id, MangaEntryId = planned.Id, ReadingStatus = "planned" });
        await db.SaveChangesAsync();

        var dashboard = await new DashboardRepository(db).GetAsync(user.Id, "en", DateTimeOffset.UtcNow.AddDays(-7), CancellationToken.None);

        Assert.NotNull(dashboard.ContinueReading);
        Assert.Equal(planned.Id, dashboard.ContinueReading!.Id);
    }

    [Fact]
    public async Task GetAsync_KeepsExternalTitleWhenTheCheckIsDue()
    {
        await using var db = TestDb.Create();
        var user = new MangaUser { Username = "reader", PasswordHash = "hash" };
        var external = new MangaEntry { Title = "Due external", FallbackReaderUrl = "https://reader.example/due" };
        db.AddRange(user, external);
        db.UserMangaEntries.Add(new UserMangaEntry
        {
            UserId = user.Id,
            MangaEntryId = external.Id,
            ReadingStatus = "reading",
            CurrentChapter = "38",
            ExternalReaderLatestChapter = "38",
            LastExternalReaderVerifiedAt = DateTimeOffset.UtcNow.AddDays(-8)
        });
        await db.SaveChangesAsync();

        var dashboard = await new DashboardRepository(db).GetAsync(user.Id, "en", DateTimeOffset.UtcNow.AddDays(-7), CancellationToken.None);

        Assert.Equal(external.Id, dashboard.ContinueReading!.Id);
    }
}
