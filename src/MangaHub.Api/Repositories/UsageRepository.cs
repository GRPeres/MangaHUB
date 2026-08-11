using MangaHub.Core.Models;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class UsageRepository(MangaHubDbContext db)
{
    public Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.Where(user => user.Id == userId).Select(user => user.UsageAnalyticsEnabled).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> HasIdempotencyKeyAsync(Guid userId, string key, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(key)
            ? Task.FromResult(false)
            : db.UsageEvents.AnyAsync(item => item.UserId == userId && item.IdempotencyKey == key, cancellationToken);

    public void Add(UsageEvent usageEvent) => db.UsageEvents.Add(usageEvent);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
    public IQueryable<UsageEvent> Events => db.UsageEvents;
    public IQueryable<UsageDailySummary> Summaries => db.UsageDailySummaries;
    public void RemoveEvents(IEnumerable<UsageEvent> events) => db.UsageEvents.RemoveRange(events);
    public void RemoveSummaries(IEnumerable<UsageDailySummary> summaries) => db.UsageDailySummaries.RemoveRange(summaries);
    public void AddSummary(UsageDailySummary summary) => db.UsageDailySummaries.Add(summary);
}
