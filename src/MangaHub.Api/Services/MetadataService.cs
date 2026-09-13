using MangaHub.Core.Dto;
using MangaHub.Core.Services;
using MangaHub.Core.Sources;
using MangaHub.Api.Common;

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
                "mangadex", item.Id, item.Title, "", "", null, "", item.Description,
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

    public async Task<MangaDexCatalogMatch?> FindMangaDexTitleMatchAsync(string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var exactMatch = (await SearchMangaDexAsync(title, cancellationToken))
            .FirstOrDefault(result => string.Equals(NormalizeTitle(result.Title), NormalizeTitle(title), StringComparison.OrdinalIgnoreCase));
        return exactMatch is null ? null : new MangaDexCatalogMatch(exactMatch.SourceId, exactMatch.Title);
    }

    public async Task<MetadataResult?> GetMangaDexMetadataAsync(string mangaDexId, CancellationToken cancellationToken)
    {
        var source = sources.FirstOrDefault(item => string.Equals(item.Name, "mangadex", StringComparison.OrdinalIgnoreCase));
        var normalizedId = TextRules.ExtractMangaDexId(mangaDexId);
        if (source is null || string.IsNullOrWhiteSpace(normalizedId)) return null;

        try
        {
            var series = await source.GetSeriesAsync(normalizedId, cancellationToken);
            if (series is null) return null;
            var fallbackCover = await FindFallbackCoverAsync(series.Title, cancellationToken);
            return new MetadataResult(
                "mangadex", series.Id, series.Title, "", fallbackCover, series.FirstPublishYear, series.Category, series.Description,
                "", series.Status, null, null, "", "", series.AlternateTitles ?? []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return null;
        }
    }

    public async Task<MetadataResult?> GetMangaUpdatesMetadataAsync(string mangaUpdatesId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mangaUpdatesId)) return null;

        try
        {
            var details = await mangaUpdates.GetSeriesAsync(mangaUpdatesId.Trim(), cancellationToken);
            if (details is null) return null;
            var match = (await mangaUpdates.SearchSeriesAsync(details.Title, cancellationToken))
                .FirstOrDefault(item => string.Equals(item.Id, details.Id, StringComparison.OrdinalIgnoreCase));
            return new MetadataResult(
                "mangaupdates", details.Id, details.Title, "", "", match?.Year, match?.Type ?? "", "",
                match?.Type ?? "", details.Status, null, null, "", "", match?.AlternativeTitles?.ToList() ?? []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string NormalizeTitle(string title)
    {
        var chars = title.Trim().ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();
        return new string(chars);
    }

    private async Task<string> FindFallbackCoverAsync(string title, CancellationToken cancellationToken)
    {
        try
        {
            var normalizedTitle = NormalizeTitle(title);
            var malCover = (await myAnimeList.SearchMangaAsync(title, cancellationToken))
                .OrderBy(item => string.Equals(NormalizeTitle(item.Title), normalizedTitle, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .Select(item => item.CoverUrl)
                .FirstOrDefault(IsUsableCoverUrl);
            if (!string.IsNullOrWhiteSpace(malCover)) return malCover;

            return (await openLibrary.SearchAsync(title, cancellationToken))
                .OrderBy(item => string.Equals(NormalizeTitle(item.Title), normalizedTitle, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .Select(item => item.CoverUrl)
                .FirstOrDefault(IsUsableCoverUrl) ?? "";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return "";
        }
    }

    private static bool IsUsableCoverUrl(string url) => !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && !uri.Host.EndsWith("mangadex.org", StringComparison.OrdinalIgnoreCase);
}
