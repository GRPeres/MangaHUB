using MangaHub.Core.Models;
using MangaHub.Infrastructure.Data;

namespace MangaHub.Api.Services;

public sealed class ArchiveRecoveryTelemetryService(MangaHubDbContext db)
{
    public async Task RecordAsync(string mangaDexId, string chapterSourceId, string action, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mangaDexId) || string.IsNullOrWhiteSpace(chapterSourceId)) return;

        db.ArchiveRecoveryEvents.Add(new ArchiveRecoveryEvent
        {
            MangaDexId = mangaDexId,
            ChapterSourceId = chapterSourceId,
            Action = action,
            OccurredAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
