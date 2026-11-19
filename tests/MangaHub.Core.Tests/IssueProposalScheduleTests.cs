using MangaHub.Core.Models;

namespace MangaHub.Core.Tests;

public sealed class IssueProposalScheduleTests
{
    [Fact]
    public void DueBatchWaitsUntilAllDraftsAreReviewed()
    {
        var now = DateTimeOffset.UtcNow;
        var schedule = new IssueProposalSchedule { Enabled = true, NextRunAt = now.AddHours(-1) };
        Assert.False(schedule.CanStart(now, hasUnreadDrafts: true));
        Assert.True(schedule.CanStart(now, hasUnreadDrafts: false));
    }

    [Fact]
    public void DisabledEarlyAndInFlightBatchesCannotStart()
    {
        var now = DateTimeOffset.UtcNow;
        var schedule = new IssueProposalSchedule();
        Assert.False(schedule.CanStart(now, false));
        schedule.Enabled = true;
        schedule.NextRunAt = now.AddMinutes(1);
        Assert.False(schedule.CanStart(now, false));
        schedule.RunId = Guid.NewGuid();
        Assert.False(schedule.CanStart(now.AddMinutes(2), false));
    }
}
