using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class ProgressRepository(MangaHubDbContext db)
{
    public Task<ReadingProgress?> GetAsync(Guid userId, Guid seriesId, CancellationToken cancellationToken) =>
        db.ReadingProgress.FirstOrDefaultAsync(x => x.UserId == userId && x.SeriesId == seriesId, cancellationToken);

    public void Add(ReadingProgress progress) => db.ReadingProgress.Add(progress);

    public Task<List<ProgressResponse>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        db.ReadingProgress.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new ProgressResponse(x.SeriesId, x.ChapterId, x.Page))
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
