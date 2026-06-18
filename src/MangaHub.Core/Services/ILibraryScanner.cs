namespace MangaHub.Core.Services;

public interface ILibraryScanner
{
    Task<LibraryScanResult> ScanAsync(CancellationToken cancellationToken);
}

public sealed record LibraryScanResult(int SeriesCount, int ChapterCount);

