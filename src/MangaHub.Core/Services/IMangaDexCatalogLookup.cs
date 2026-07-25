namespace MangaHub.Core.Services;

public interface IMangaDexCatalogLookup
{
    Task<MangaDexCatalogMatch?> FindByMyAnimeListIdAsync(string myAnimeListId, string title, CancellationToken cancellationToken);
}

public sealed record MangaDexCatalogMatch(string Id, string Title);
