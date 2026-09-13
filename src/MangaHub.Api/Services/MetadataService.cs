using MangaHub.Core.Dto;
using MangaHub.Core.Services;
using MangaHub.Core.Sources;

namespace MangaHub.Api.Services;

public sealed class MetadataService(
    IMyAnimeListClient myAnimeList,
    IOpenLibraryClient openLibrary,
    IEnumerable<IMangaSource> sources,
    IMangaUpdatesClient mangaUpdates,
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
        if (malResults.Count > 0)
        {
            return includeOpenLibrary
                ? await AddOpenLibraryResultsAsync(query, malResults, cancellationToken)
                : malResults;
        }

        var mangaDexResults = await SearchMangaDexAsync(query, cancellationToken);
        if (mangaDexResults.Count > 0)
        {
            return includeOpenLibrary
                ? await AddOpenLibraryResultsAsync(query, mangaDexResults, cancellationToken)
                : mangaDexResults;
        }

        var mangaUpdatesResults = await SearchMangaUpdatesAsync(query, cancellationToken);
        if (mangaUpdatesResults.Count > 0)
        {
            return includeOpenLibrary
                ? await AddOpenLibraryResultsAsync(query, mangaUpdatesResults, cancellationToken)
                : mangaUpdatesResults;
        }

        return await AddOpenLibraryResultsAsync(query, [], cancellationToken);
    }

    private async Task<List<MetadataResult>> SearchMangaDexAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var source = sources.FirstOrDefault(item => string.Equals(item.Name, "mangadex", StringComparison.OrdinalIgnoreCase));
            if (source is null) return [];
            var results = await source.SearchAsync(query, cancellationToken);
            return results.Select(item => new MetadataResult(
                "mangadex", item.Id, item.Title, "", item.CoverUrl, null, "", item.Description,
                "", item.Status, null, null, "", "", item.AlternateTitles ?? [])).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return [];
        }
    }

    private async Task<List<MetadataResult>> SearchMangaUpdatesAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var results = await mangaUpdates.SearchSeriesAsync(query, cancellationToken);
            return results.Select(item => new MetadataResult(
                "mangaupdates", item.Id, item.Title, "", "", item.Year, item.Type, "",
                item.Type, "", null, null, "", "", item.AlternativeTitles.ToList())).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return [];
        }
    }

    private async Task<List<MetadataResult>> AddOpenLibraryResultsAsync(string query, List<MetadataResult> primaryResults, CancellationToken cancellationToken)
    {
        var openLibraryResults = await openLibrary.SearchAsync(query, cancellationToken);
        var seenTitles = primaryResults.Select(x => NormalizeTitle(x.Title)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var combined = new List<MetadataResult>(primaryResults);

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
                "",
                item.AlternateTitles ?? []));
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
