using MangaHub.Core.Models;
using MangaHub.Core.Services;
using MangaHub.Infrastructure.Data;

namespace MangaHub.Api.Tests;

public sealed class MaintenanceFailureIssueServiceTests
{
    [Fact]
    public async Task RecordFailureAsync_RefreshesOneIssueAndSuccessfulRunResolvesIt()
    {
        await using var db = TestDb.Create();
        var service = new MaintenanceFailureIssueService(db);
        var failed = new MaintenanceJob { Type = "release-sync", Trigger = "scheduled", Status = "failed", Error = "First failure", CompletedAt = DateTimeOffset.UtcNow };
        db.MaintenanceJobs.Add(failed);
        await service.RecordFailureAsync(failed, CancellationToken.None);
        await db.SaveChangesAsync();

        failed.Error = "Newest failure";
        await service.RecordFailureAsync(failed, CancellationToken.None);
        await db.SaveChangesAsync();

        var issue = Assert.Single(db.AdminIssues);
        Assert.Equal(AdminIssueTypes.MaintenanceFailure, issue.Kind);
        Assert.Equal("high", issue.Priority);
        Assert.Contains("Newest failure", issue.MetadataJson);

        var recovered = new MaintenanceJob { Type = "release-sync", Trigger = "recovery", Status = "completed" };
        await service.ResolveAfterSuccessAsync(recovered, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal("resolved", issue.Status);
        Assert.Equal("auto_resolved", issue.ReviewState);
    }

    [Theory]
    [InlineData("Same failure", false, 1)]
    [InlineData("New failure", false, 2)]
    [InlineData("Same failure", true, 2)]
    public async Task RecordFailureAsync_RespectsDismissalUntilEvidenceChanges(string error, bool recovered, int count)
    {
        await using var db = TestDb.Create();
        var service = new MaintenanceFailureIssueService(db);
        var now = DateTimeOffset.UtcNow;
        await service.RecordFailureAsync(new MaintenanceJob { Type = "release-sync", Error = "Same failure" }, default);
        await db.SaveChangesAsync();
        var dismissed = Assert.Single(db.AdminIssues);
        dismissed.Status = "dismissed";
        dismissed.ResolvedAt = now.AddMinutes(-2);
        if (recovered)
            db.MaintenanceJobs.Add(new MaintenanceJob { Type = "release-sync", Status = "completed", CompletedAt = now.AddMinutes(-1) });
        await db.SaveChangesAsync();

        await service.RecordFailureAsync(new MaintenanceJob { Type = "release-sync", Error = error, CompletedAt = now }, default);
        await db.SaveChangesAsync();
        Assert.Equal(count, db.AdminIssues.Count());
        Assert.Equal("dismissed", dismissed.Status);
    }
}
