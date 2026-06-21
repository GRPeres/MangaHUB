using MangaHub.Core.Dto;
using MangaHub.Core.Services;

namespace MangaHub.Api.Services;

public sealed class LibraryService(ILibraryScanner scanner)
{
    public Task<LibraryScanResult> ScanAsync(CancellationToken cancellationToken) =>
        scanner.ScanAsync(cancellationToken);
}
