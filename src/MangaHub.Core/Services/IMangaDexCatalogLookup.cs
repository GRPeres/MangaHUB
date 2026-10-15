namespace MangaHub.Core.Services;

public interface IMangaDexCatalogLookup
{
    Task<MangaDexCatalogMatch?> FindByMyAnimeListIdAsync(string myAnimeListId, string title, CancellationToken cancellationToken);
    Task<MangaDexCatalogIdentity?> GetIdentityAsync(string mangaDexId, CancellationToken cancellationToken);
}

public sealed record MangaDexCatalogMatch(string Id, string Title);
public sealed record MangaDexCatalogIdentity(string Id, string Title, string MyAnimeListId, List<string> AlternateTitles);
