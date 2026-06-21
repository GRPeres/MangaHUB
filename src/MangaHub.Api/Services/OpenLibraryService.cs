using MangaHub.Core.Dto;
using MangaHub.Core.Services;

namespace MangaHub.Api.Services;

public sealed class OpenLibraryService(IOpenLibraryClient openLibrary)
{
    public async Task<List<OpenLibraryResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var results = await openLibrary.SearchAsync(query, cancellationToken);
        return results
            .Select(x => new OpenLibraryResult(x.Key, x.Title, x.Authors, x.CoverUrl, x.FirstPublishYear, x.Category, x.Description))
            .ToList();
    }
}
