namespace MangaHub.Web.Services;

public sealed class MangaHubApiClient(
    AuthApiService auth,
    AdminApiService admin,
    CatalogApiService catalog,
    OpenLibraryApiService openLibrary,
    MangaApiService manga,
    ShelfApiService shelf,
    LibraryApiService library,
    SeriesApiService series,
    ReadApiService read)
{
    public Task<UserResponse?> RegisterAsync(string username, string password) =>
        auth.RegisterAsync(username, password);

    public Task<UserResponse?> LoginAsync(string username, string password) =>
        auth.LoginAsync(username, password);

    public Task LogoutAsync() => auth.LogoutAsync();

    public Task<UserResponse?> MeAsync() => auth.MeAsync();

    public Task<List<UserAdminResponse>> GetUsersAsync() => admin.GetUsersAsync();

    public Task<UserAdminResponse?> UpdateUserRoleAsync(Guid userId, string role) =>
        admin.UpdateUserRoleAsync(userId, role);

    public Task<List<SeriesResponse>> GetSeriesAsync() => series.GetSeriesAsync();

    public Task<List<MangaEntryResponse>> GetMangaEntriesAsync(string? status = null, Guid? userId = null) =>
        manga.GetMangaEntriesAsync(status, userId);

    public Task<List<CatalogMangaResponse>> GetCatalogAsync(string? queryText = null) =>
        catalog.GetCatalogAsync(queryText);

    public Task<List<OpenLibraryResult>> SearchOpenLibraryAsync(string query) =>
        openLibrary.SearchOpenLibraryAsync(query);

    public Task<CatalogMangaResponse?> CreateCatalogMangaAsync(MangaEntryRequest request) =>
        catalog.CreateCatalogMangaAsync(request);

    public Task<CatalogMangaResponse?> UpdateCatalogMangaAsync(Guid entryId, MangaEntryRequest request) =>
        catalog.UpdateCatalogMangaAsync(entryId, request);

    public Task<MangaEntryResponse?> AddToShelfAsync(AddToShelfRequest request) =>
        shelf.AddToShelfAsync(request);

    public Task<MangaEntryResponse?> UpdateShelfAsync(Guid entryId, AddToShelfRequest request, Guid? userId = null) =>
        shelf.UpdateShelfAsync(entryId, request, userId);

    public Task<bool> RemoveShelfAsync(Guid entryId, Guid? userId = null) =>
        shelf.RemoveShelfAsync(entryId, userId);

    public Task<ShelfImportResponse?> ImportShelfAsync(ShelfImportRequest request) =>
        shelf.ImportShelfAsync(request);

    public Task<ReadOptions?> GetReadOptionsAsync(Guid entryId) =>
        manga.GetReadOptionsAsync(entryId);

    public Task<SeriesResponse?> GetSeriesAsync(Guid id) =>
        series.GetSeriesAsync(id);

    public Task<List<ChapterResponse>> GetChaptersAsync(Guid seriesId) =>
        series.GetChaptersAsync(seriesId);

    public Task<List<SearchResult>> SearchAsync(string query) =>
        series.SearchAsync(query);

    public Task<LibraryScanResult?> ScanAsync() =>
        library.ScanAsync();

    public string GetPageUrl(Guid chapterId, int pageIndex) =>
        read.GetPageUrl(chapterId, pageIndex);
}

public sealed record AuthRequest(string Username, string Password);
public sealed record UserResponse(Guid Id, string Username, string Role, string SessionToken);
public sealed record UserAdminResponse(Guid Id, string Username, string Role, DateTimeOffset CreatedAt);
public sealed record UpdateUserRoleRequest(string Role);
public sealed record OpenLibraryResult(string Key, string Title, string Authors, string CoverUrl, int? FirstPublishYear, string Category, string Description);
public sealed record MangaEntryRequest(
    string Title,
    string Authors,
    string Category,
    string Description,
    string CoverUrl,
    string OpenLibraryKey,
    int? FirstPublishYear,
    string ReadingStatus,
    string MangaDexUrl,
    Guid? LocalSeriesId,
    string Notes);
public sealed record MangaEntryResponse(
    Guid Id,
    string Title,
    string Authors,
    string CatalogCategory,
    string Description,
    string CoverUrl,
    string OpenLibraryKey,
    int? FirstPublishYear,
    string ReadingStatus,
    string MangaDexUrl,
    string MangaDexId,
    Guid? LocalSeriesId,
    string CurrentChapter,
    int? Score,
    string Category,
    string Summary,
    string Notes);
public sealed record CatalogMangaResponse(
    Guid Id,
    string Title,
    string Authors,
    string Category,
    string Description,
    string CoverUrl,
    string OpenLibraryKey,
    int? FirstPublishYear,
    string MangaDexUrl,
    string MangaDexId,
    Guid? LocalSeriesId,
    bool IsInMyShelf);
public sealed record AddToShelfRequest(
    Guid MangaEntryId,
    string ReadingStatus,
    string CurrentChapter,
    int? Score,
    string Category,
    string Summary,
    string Notes);
public sealed record ShelfImportRequest(string CsvText, bool CreateMissingCatalogEntries);
public sealed record ShelfImportResponse(int Imported, int CreatedCatalogEntries, int UpdatedShelfEntries, int Skipped, List<string> Messages);
public sealed record ReadOptions(
    Guid Id,
    string Title,
    bool HasMangaDex,
    string MangaDexUrl,
    bool HasLocal,
    string LocalReaderUrl);
public sealed record SeriesResponse(Guid Id, string Title, string Description, string CoverUrl, string Status, string Source, string ExternalId);
public sealed record ChapterResponse(Guid Id, Guid SeriesId, string ChapterNumber, string Title, int PageCount);
public sealed record SearchResult(string Id, string Title, string Description, string CoverUrl, string Status, string Source);
public sealed record LibraryScanResult(int SeriesCount, int ChapterCount);
public sealed record ProgressRequest(Guid SeriesId, Guid ChapterId, int Page);
public sealed record ProgressResponse(Guid SeriesId, Guid ChapterId, int Page);
