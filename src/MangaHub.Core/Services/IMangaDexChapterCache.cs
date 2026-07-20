using MangaHub.Core.Sources;

namespace MangaHub.Core.Services;

public interface IMangaDexChapterCache
{
    Task<MangaDexCachedChapter> EnsureCachedAsync(
        string mangaDexId,
        string chapterId,
        IReadOnlyList<MangaPage> pages,
        CancellationToken cancellationToken,
        IProgress<ReaderPreparationProgress>? progress = null);

    Task<MangaDexCachedChapter> ImportAsync(
        string mangaDexId,
        string chapterId,
        Stream content,
        CancellationToken cancellationToken);

    Task DeleteAsync(string mangaDexId, string chapterId, CancellationToken cancellationToken);
}

public sealed record MangaDexCachedChapter(string RelativePath, int PageCount, string FileHash, bool WasCached);
public sealed record ReaderPreparationProgress(string Stage, int Progress, int CompletedPages = 0, int TotalPages = 0);
