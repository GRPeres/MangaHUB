namespace MangaHub.Core.Services;

public interface IOpenLibraryClient
{
    Task<IReadOnlyList<OpenLibrarySearchResult>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<OpenLibraryWorkDetails?> GetWorkAsync(string key, CancellationToken cancellationToken);
}

public sealed record OpenLibrarySearchResult(
    string Key,
    string Title,
    string Authors,
    string CoverUrl,
    int? FirstPublishYear,
    string Category,
    string Description);

public sealed record OpenLibraryWorkDetails(string Category, string Description);
