using MangaHub.Core.Dto;
using MangaHub.Core.Services;

namespace MangaHub.Api.Services;

public sealed class MetadataService(
    IMyAnimeListClient myAnimeList,
    IOpenLibraryClient openLibrary,
    MangaDexCatalogMatchService mangaDexMatches,
    MangaUpdatesCatalogMatchService mangaUpdatesMatches)
{
    public async Task<List<MetadataResult>> SearchAsync(string query, bool includeOpenLibrary, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var malResults = (await myAnimeList.SearchMangaAsync(query, cancellationToken)).ToList();
        var shouldSearchOpenLibrary = includeOpenLibrary || malResults.Count == 0;
        if (!shouldSearchOpenLibrary)
        {
            return malResults;
        }

        var openLibraryResults = await openLibrary.SearchAsync(query, cancellationToken);
        var seenTitles = malResults.Select(x => NormalizeTitle(x.Title)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var combined = new List<MetadataResult>(malResults);

        foreach (var item in openLibraryResults)
        {
            var normalized = NormalizeTitle(item.Title);
            if (string.IsNullOrWhiteSpace(normalized) || seenTitles.Contains(normalized))
            {
                continue;
            }

            seenTitles.Add(normalized);
            combined.Add(new MetadataResult(
                "openlibrary",
                item.Key,
                item.Title,
                item.Authors,
                item.CoverUrl,
                item.FirstPublishYear,
                item.Category,
                item.Description,
                "",
                "",
                null,
                null,
                item.Key,
                ""));
        }

        return combined;
    }

    public Task<MangaDexCatalogMatch?> FindMangaDexMatchAsync(string myAnimeListId, string title, CancellationToken cancellationToken) =>
        mangaDexMatches.FindAsync(myAnimeListId, title, cancellationToken);

    public Task<MangaUpdatesSearchResult?> FindMangaUpdatesMatchAsync(string title, string mediaType, int? firstPublishYear, CancellationToken cancellationToken) =>
        mangaUpdatesMatches.FindAsync(title, mediaType, firstPublishYear, cancellationToken);

    private static string NormalizeTitle(string title)
    {
        var chars = title.Trim().ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }
}
