using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Services;
using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Services;

public sealed class ReaderService(
    ShelfRepository shelf,
    SeriesRepository series,
    IArchiveReader archives,
    IOptions<MangaHubOptions> options)
{
    public async Task<ReadOptions?> GetReadOptionsAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        var shelfEntry = await shelf.GetReadShelfAsync(userId, entryId, cancellationToken);
        if (shelfEntry?.MangaEntry is null)
        {
            return null;
        }

        var entry = shelfEntry.MangaEntry;
        (Guid Id, int PageCount)? localFirstChapter = entry.LocalSeriesId is null
            ? null
            : await series.GetFirstChapterAsync(entry.LocalSeriesId.Value, cancellationToken);

        return new ReadOptions(
            entry.Id,
            entry.Title,
            !string.IsNullOrWhiteSpace(entry.MangaDexUrl),
            entry.MangaDexUrl,
            localFirstChapter is not null,
            localFirstChapter is null ? "" : $"/reader/{localFirstChapter.Value.Id}/{localFirstChapter.Value.PageCount}");
    }

    public async Task<ArchivePage?> GetPageAsync(Guid chapterId, int pageIndex, CancellationToken cancellationToken)
    {
        var chapter = await series.GetChapterWithSeriesAsync(chapterId, cancellationToken);
        if (chapter?.Series?.Source != "local")
        {
            return null;
        }

        var root = Path.GetFullPath(options.Value.LibraryPath);
        var archivePath = Path.GetFullPath(Path.Combine(root, chapter.SourceId));
        if (!archivePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await archives.ReadPageAsync(archivePath, pageIndex, cancellationToken);
    }
}

public sealed record ReadOptions(
    Guid Id,
    string Title,
    bool HasMangaDex,
    string MangaDexUrl,
    bool HasLocal,
    string LocalReaderUrl);
