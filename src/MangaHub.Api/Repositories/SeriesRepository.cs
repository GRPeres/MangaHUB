using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class SeriesRepository(MangaHubDbContext db)
{
    public async Task<List<SeriesResponse>> ListAsync(string? title, string? source, string? status, CancellationToken cancellationToken)
    {
        var query = db.Series.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(title))
        {
            query = query.Where(x => EF.Functions.ILike(x.Title, $"%{title}%"));
        }
        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(x => x.Source == source);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        return await query.OrderBy(x => x.Title)
            .Select(x => new SeriesResponse(x.Id, x.Title, x.Description, x.CoverUrl, x.Status, x.Source, x.ExternalId))
            .ToListAsync(cancellationToken);
    }

    public Task<List<object>> SearchLocalAsync(string queryText, CancellationToken cancellationToken) =>
        db.Series.AsNoTracking()
            .Where(x => EF.Functions.ILike(x.Title, $"%{queryText}%"))
            .Take(25)
            .Select(x => new { x.Id, x.Title, x.Description, x.CoverUrl, x.Status, x.Source })
            .Cast<object>()
            .ToListAsync(cancellationToken);

    public async Task<SeriesResponse?> GetAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        var series = await db.Series.AsNoTracking().FirstOrDefaultAsync(x => x.Id == seriesId, cancellationToken);
        return series is null
            ? null
            : new SeriesResponse(series.Id, series.Title, series.Description, series.CoverUrl, series.Status, series.Source, series.ExternalId);
    }

    public Task<MangaChapter?> GetChapterWithSeriesAsync(Guid chapterId, CancellationToken cancellationToken) =>
        db.Chapters.Include(x => x.Series).FirstOrDefaultAsync(x => x.Id == chapterId, cancellationToken);

    public Task<MangaSeries?> GetBySourceAndExternalIdAsync(string source, string externalId, CancellationToken cancellationToken) =>
        db.Series.Include(x => x.Chapters)
            .FirstOrDefaultAsync(x => x.Source == source && x.ExternalId == externalId, cancellationToken);

    public void AddSeries(MangaSeries series) => db.Series.Add(series);

    public void AddChapter(MangaChapter chapter) => db.Chapters.Add(chapter);

    public void RemoveChapter(MangaChapter chapter) => db.Chapters.Remove(chapter);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public Task<List<ChapterResponse>> ListChaptersAsync(Guid seriesId, CancellationToken cancellationToken) =>
        db.Chapters.AsNoTracking()
            .Where(x => x.SeriesId == seriesId)
            .OrderBy(x => x.ChapterNumber)
            .Select(x => new ChapterResponse(x.Id, x.SeriesId, x.ChapterNumber, x.Title, x.PageCount))
            .ToListAsync(cancellationToken);

    public async Task<(Guid Id, int PageCount)?> GetFirstChapterAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        var chapter = await db.Chapters.AsNoTracking()
            .Where(x => x.SeriesId == seriesId)
            .OrderBy(x => x.ChapterNumber)
            .Select(x => new { x.Id, x.PageCount })
            .FirstOrDefaultAsync(cancellationToken);

        return chapter is null ? null : (chapter.Id, chapter.PageCount);
    }
}
