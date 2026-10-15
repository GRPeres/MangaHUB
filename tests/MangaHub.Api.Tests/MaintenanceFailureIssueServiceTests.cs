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
    }
}
