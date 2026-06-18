namespace MangaHub.Core.Services;

public interface IOpenLibraryClient
{
    Task<IReadOnlyList<OpenLibrarySearchResult>> SearchAsync(string query, CancellationToken cancellationToken);
}

public sealed record OpenLibrarySearchResult(
    string Key,
    string Title,
    string Authors,
    string CoverUrl,
    int? FirstPublishYear);

