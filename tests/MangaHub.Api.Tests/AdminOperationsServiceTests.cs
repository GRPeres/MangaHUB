using MangaHub.Api.Services;
using MangaHub.Core.Models;
using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Tests;

public sealed class AdminOperationsServiceTests
{
    [Fact]
    public async Task ListHistoryAsync_AppliesTypeStatusAndTriggerFiltersBeforePaging()
    {
        await using var db = TestDb.Create();
        db.MaintenanceJobs.AddRange(
            new MaintenanceJob { Type = "release-sync", Status = "completed", Trigger = "scheduled", RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-2) },
            new MaintenanceJob { Type = "release-sync", Status = "failed", Trigger = "manual", RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
            new MaintenanceJob { Type = "library-scan", Status = "completed", Trigger = "scheduled", RequestedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var service = new AdminOperationsService(db, Options.Create(new MangaHubOptions()));

        var history = await service.ListHistoryAsync(0, 25, " RELEASE-SYNC ", "COMPLETED", "SCHEDULED", CancellationToken.None);

        var job = Assert.Single(history);
        Assert.Equal("release-sync", job.Type);
        Assert.Equal("completed", job.Status);
        Assert.Equal("scheduled", job.Trigger);
    }
}
