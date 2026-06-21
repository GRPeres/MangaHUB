using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;

namespace MangaHub.Api.Services;

public sealed class ProgressService(ProgressRepository progressRepository)
{
    public async Task<ProgressResponse> SaveAsync(Guid userId, ProgressRequest progress, CancellationToken cancellationToken)
    {
        var existing = await progressRepository.GetAsync(userId, progress.SeriesId, cancellationToken);
        if (existing is null)
        {
            progressRepository.Add(new ReadingProgress
            {
                UserId = userId,
                SeriesId = progress.SeriesId,
                ChapterId = progress.ChapterId,
                Page = progress.Page
            });
        }
        else
        {
            existing.ChapterId = progress.ChapterId;
            existing.Page = progress.Page;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await progressRepository.SaveChangesAsync(cancellationToken);
        return new ProgressResponse(progress.SeriesId, progress.ChapterId, progress.Page);
    }

    public Task<List<ProgressResponse>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        progressRepository.ListAsync(userId, cancellationToken);
}
