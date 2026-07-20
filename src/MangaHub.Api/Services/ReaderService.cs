using System.Globalization;
using MangaHub.Api.Common;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Sources;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Services;

public sealed class ReaderService(
    ShelfRepository shelf,
    SeriesRepository series,
    IArchiveReader archives,
    IOptions<MangaHubOptions> options,
    MangaSourceRegistry sources,
    IHttpClientFactory httpClientFactory)
{
    public async Task<ReadOptions?> GetReadOptionsAsync(Guid userId, Guid entryId, CancellationToken cancellationToken)
    {
        var shelfEntry = await shelf.GetReadShelfAsync(userId, entryId, cancellationToken);
        if (shelfEntry?.MangaEntry is null)
        {
            return null;
        }

        var entry = shelfEntry.MangaEntry;
        var mangaDexId = GetMangaDexId(entry);
        (Guid Id, int PageCount)? localFirstChapter = entry.LocalSeriesId is null
            ? null
            : await series.GetFirstChapterAsync(entry.LocalSeriesId.Value, cancellationToken);

        return new ReadOptions(
            entry.Id,
            entry.Title,
            !string.IsNullOrWhiteSpace(mangaDexId),
            entry.MangaDexUrl,
            localFirstChapter is not null,
            localFirstChapter is null ? "" : $"/reader/{localFirstChapter.Value.Id}/{localFirstChapter.Value.PageCount}",
            string.IsNullOrWhiteSpace(mangaDexId) ? "" : $"/reader/mangadex/{entry.Id}");
    }

    public async Task<MangaDexReaderSession?> GetMangaDexReaderSessionAsync(Guid userId, Guid entryId, string? requestedChapterId, CancellationToken cancellationToken)
    {
        var shelfEntry = await shelf.GetReadShelfAsync(userId, entryId, cancellationToken);
        if (shelfEntry?.MangaEntry is null)
        {
            return null;
        }

        var mangaDexId = GetMangaDexId(shelfEntry.MangaEntry);
        if (string.IsNullOrWhiteSpace(mangaDexId))
        {
            return null;
        }

        var mangaDex = sources.Get("mangadex");
        var chapters = await mangaDex.GetChaptersAsync(mangaDexId, cancellationToken);
        var selected = SelectChapter(chapters, requestedChapterId, shelfEntry.CurrentChapter);
        if (selected is null)
        {
            return null;
        }

        var pages = await mangaDex.GetPagesAsync(selected.Id, cancellationToken);
        if (pages.Count == 0)
        {
            return null;
        }

        var readerChapters = chapters
            .Select(chapter => new MangaDexReaderChapter(chapter.Id, chapter.Number, chapter.Title, chapter.PageCount))
            .ToList();
        var selectedChapter = new MangaDexReaderChapter(selected.Id, selected.Number, selected.Title, pages.Count);

        return new MangaDexReaderSession(
            shelfEntry.MangaEntry.Id,
            shelfEntry.MangaEntry.Title,
            shelfEntry.CurrentChapter,
            selectedChapter,
            readerChapters);
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

    public async Task<ArchivePage?> GetMangaDexPageAsync(Guid userId, Guid entryId, string chapterId, int pageIndex, CancellationToken cancellationToken)
    {
        var shelfEntry = await shelf.GetReadShelfAsync(userId, entryId, cancellationToken);
        if (shelfEntry?.MangaEntry is null || pageIndex < 0)
        {
            return null;
        }

        var mangaDexId = GetMangaDexId(shelfEntry.MangaEntry);
        if (string.IsNullOrWhiteSpace(mangaDexId))
        {
            return null;
        }

        var mangaDex = sources.Get("mangadex");
        var chapters = await mangaDex.GetChaptersAsync(mangaDexId, cancellationToken);
        if (!chapters.Any(chapter => string.Equals(chapter.Id, chapterId, StringComparison.Ordinal)))
        {
            return null;
        }

        var pages = await mangaDex.GetPagesAsync(chapterId, cancellationToken);
        if (pageIndex >= pages.Count || !IsMangaDexImageUrl(pages[pageIndex].Url))
        {
            return null;
        }

        using var response = await httpClientFactory.CreateClient("mangadex-pages")
            .GetAsync(pages[pageIndex].Url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(contentType) || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new ArchivePage($"mangadex-{chapterId}-{pageIndex}", contentType, bytes);
    }

    public async Task<MangaDexReaderProgressResponse?> SaveMangaDexProgressAsync(
        Guid userId,
        Guid entryId,
        MangaDexReaderProgressRequest request,
        CancellationToken cancellationToken)
    {
        var shelfEntry = await shelf.GetWithMangaAsync(userId, entryId, cancellationToken);
        if (shelfEntry?.MangaEntry is null || string.IsNullOrWhiteSpace(request.ChapterId))
        {
            return null;
        }

        var mangaDexId = GetMangaDexId(shelfEntry.MangaEntry);
        if (string.IsNullOrWhiteSpace(mangaDexId))
        {
            return null;
        }

        var mangaDex = sources.Get("mangadex");
        var chapter = (await mangaDex.GetChaptersAsync(mangaDexId, cancellationToken))
            .FirstOrDefault(candidate => string.Equals(candidate.Id, request.ChapterId, StringComparison.Ordinal));
        if (chapter is null)
        {
            return null;
        }

        if (request.Completed)
        {
            shelfEntry.CurrentChapter = chapter.Number;
            if (string.Equals(shelfEntry.ReadingStatus, "planned", StringComparison.OrdinalIgnoreCase))
            {
                shelfEntry.ReadingStatus = "reading";
            }
            shelfEntry.UpdatedAt = DateTimeOffset.UtcNow;
            await shelf.SaveChangesAsync(cancellationToken);
        }

        return new MangaDexReaderProgressResponse(shelfEntry.CurrentChapter, shelfEntry.ReadingStatus, Math.Max(0, request.Page), request.Completed);
    }

    private static string GetMangaDexId(MangaHub.Core.Models.MangaEntry entry) =>
        string.IsNullOrWhiteSpace(entry.MangaDexId) ? TextRules.ExtractMangaDexId(entry.MangaDexUrl) : entry.MangaDexId;

    private static MangaSourceChapter? SelectChapter(
        IReadOnlyList<MangaSourceChapter> chapters,
        string? requestedChapterId,
        string currentChapter)
    {
        if (!string.IsNullOrWhiteSpace(requestedChapterId))
        {
            return chapters.FirstOrDefault(chapter => string.Equals(chapter.Id, requestedChapterId, StringComparison.Ordinal));
        }

        if (TryParseChapterNumber(currentChapter, out var current))
        {
            return chapters.FirstOrDefault(chapter => TryParseChapterNumber(chapter.Number, out var number) && number > current)
                ?? chapters.LastOrDefault();
        }

        return chapters.FirstOrDefault();
    }

    private static bool TryParseChapterNumber(string value, out decimal number) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out number);

    private static bool IsMangaDexImageUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && (uri.Host.EndsWith(".mangadex.org", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".mangadex.network", StringComparison.OrdinalIgnoreCase));
}

public sealed record ReadOptions(
    Guid Id,
    string Title,
    bool HasMangaDex,
    string MangaDexUrl,
    bool HasLocal,
    string LocalReaderUrl,
    string MangaDexReaderUrl);
