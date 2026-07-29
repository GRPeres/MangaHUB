using System.Text.Json;

namespace MangaHub.Core.Services;

public sealed class MangaUpdatesCatalogMatchService(IMangaUpdatesClient mangaUpdates)
{
    public async Task<MangaUpdatesSearchResult?> FindAsync(string title, string mediaType, int? firstPublishYear, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        try
        {
            var normalizedTitle = Normalize(title);
            var candidates = await mangaUpdates.SearchSeriesAsync(title, cancellationToken);
            return candidates
                .Where(candidate => IsExactTitleMatch(candidate, normalizedTitle))
                .OrderByDescending(candidate => TypeScore(candidate.Type, mediaType))
                .ThenByDescending(candidate => YearScore(candidate.Year, firstPublishYear))
                .ThenBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsExactTitleMatch(MangaUpdatesSearchResult candidate, string normalizedTitle) =>
        Normalize(candidate.Title) == normalizedTitle || candidate.AlternativeTitles.Any(title => Normalize(title) == normalizedTitle);

    private static int TypeScore(string candidateType, string mediaType) =>
        string.Equals(Normalize(candidateType), Normalize(mediaType), StringComparison.Ordinal) ? 1 : 0;

    private static int YearScore(int? candidateYear, int? firstPublishYear) =>
        candidateYear is not null && firstPublishYear is not null && Math.Abs(candidateYear.Value - firstPublishYear.Value) <= 1 ? 1 : 0;

    private static string Normalize(string value) => new(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
