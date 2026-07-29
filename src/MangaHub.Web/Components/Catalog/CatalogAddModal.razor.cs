using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Components.Catalog;

public partial class CatalogAddModal
{
    [Inject] private CatalogApiService CatalogApi { get; set; } = default!;
    [Inject] private MetadataApiService MetadataApi { get; set; } = default!;

    [Parameter] public bool Open { get; set; }
    [Parameter] public EventCallback<bool> OpenChanged { get; set; }
    [Parameter] public CatalogMangaResponse? Entry { get; set; }
    [Parameter] public List<SeriesResponse> LocalSeries { get; set; } = [];
    [Parameter] public EventCallback<CatalogMangaResponse> OnSaved { get; set; }

    private string title = "";
    private string authors = "";
    private string category = "";
    private string description = "";
    private string coverUrl = "";
    private string metadataSource = "";
    private string myAnimeListId = "";
    private string openLibraryKey = "";
    private int? firstPublishYear;
    private string mediaType = "";
    private string publishingStatus = "";
    private int? chapterCount;
    private int? volumeCount;
    private string mangaDexId = "";
    private string fallbackReaderUrl = "";
    private string readerPreference = "mangahub";
    private string mangaUpdatesId = "";
    private string localSeriesIdText = "";
    private string message = "";
    private Severity messageSeverity = Severity.Info;
    private string metadataMessage = "";
    private Severity metadataSeverity = Severity.Info;
    private bool isSearchingMetadata;
    private bool isMatchingMangaDex;
    private bool isSaving;
    private int metadataSearchVersion;
    private List<MetadataResult> metadataResults = [];
    private Guid? loadedEntryId;
    private bool wasOpen;
    private bool IsEditMode => Entry is not null;

    protected override void OnParametersSet()
    {
        if (!Open)
        {
            wasOpen = false;
            return;
        }

        if (Entry is { } entry && (!wasOpen || loadedEntryId != entry.Id))
        {
            LoadEntry(entry);
        }
        else if (Entry is null && (!wasOpen || loadedEntryId is not null))
        {
            Reset();
        }

        wasOpen = true;
    }

    private async Task SearchMetadata(string query)
    {
        var searchVersion = ++metadataSearchVersion;
        if (string.IsNullOrWhiteSpace(query))
        {
            metadataResults = [];
            metadataSeverity = Severity.Info;
            metadataMessage = "Start typing a title to search metadata.";
            return;
        }

        isSearchingMetadata = true;
        try
        {
            var results = await MetadataApi.SearchAsync(query);
            if (searchVersion != metadataSearchVersion)
            {
                return;
            }

            if (results.Count == 0)
            {
                results = await MetadataApi.SearchAsync(query, includeOpenLibrary: true);
                if (searchVersion != metadataSearchVersion)
                {
                    return;
                }
            }

            metadataResults = results;
            metadataSeverity = metadataResults.Count == 0 ? Severity.Warning : Severity.Success;
            metadataMessage = metadataResults.Count == 0
                ? "No metadata matches found."
                : $"Found {metadataResults.Count} metadata matches.";
        }
        catch
        {
            if (searchVersion != metadataSearchVersion)
            {
                return;
            }

            metadataResults = [];
            metadataSeverity = Severity.Error;
            metadataMessage = "Metadata search failed.";
        }
        finally
        {
            if (searchVersion == metadataSearchVersion)
            {
                isSearchingMetadata = false;
            }
        }
    }

    private async Task ApplyMetadata(MetadataResult item)
    {
        title = item.Title;
        authors = item.Authors;
        category = item.Category;
        description = item.Description;
        coverUrl = item.CoverUrl;
        metadataSource = item.Source;
        myAnimeListId = item.MyAnimeListId;
        openLibraryKey = item.OpenLibraryKey;
        firstPublishYear = item.FirstPublishYear;
        mediaType = item.MediaType;
        publishingStatus = item.PublishingStatus;
        chapterCount = item.ChapterCount;
        volumeCount = item.VolumeCount;
        metadataResults = [];
        metadataMessage = "";
        if (string.IsNullOrWhiteSpace(mangaUpdatesId))
        {
            await MatchMangaUpdatesAsync();
        }
        if (!string.Equals(item.Source, "myanimelist", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(item.MyAnimeListId)
            || !string.IsNullOrWhiteSpace(mangaDexId))
        {
            messageSeverity = Severity.Success;
            message = $"Filled the form from {item.Title}.";
            return;
        }

        isMatchingMangaDex = true;
        messageSeverity = Severity.Info;
        message = "Looking for the matching MangaDex title...";
        try
        {
            var match = await MetadataApi.FindMangaDexMatchAsync(item.MyAnimeListId, item.Title);
            if (match is null)
            {
                messageSeverity = Severity.Success;
                message = $"Filled the form from {item.Title}. No MangaDex match was found.";
                return;
            }

            mangaDexId = match.Id;
            messageSeverity = Severity.Success;
            message = $"Filled the form from {item.Title} and linked MangaDex: {match.Title}.";
        }
        catch
        {
            messageSeverity = Severity.Warning;
            message = $"Filled the form from {item.Title}, but MangaDex could not be checked.";
        }
        finally
        {
            isMatchingMangaDex = false;
        }
    }

    private async Task Save()
    {
        if (isSaving)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            messageSeverity = Severity.Warning;
            message = "Title is required.";
            return;
        }

        if ((readerPreference == "external" || readerPreference == "hybrid")
            && !IsHttpUrl(fallbackReaderUrl))
        {
            messageSeverity = Severity.Warning;
            message = "External and hybrid reading modes need a valid fallback reader URL.";
            return;
        }

        isSaving = true;
        messageSeverity = Severity.Info;
        message = "Adding catalog manga...";
        try
        {
            var saved = IsEditMode
                ? await CatalogApi.UpdateCatalogMangaAsync(Entry!.Id, BuildRequest())
                : await CatalogApi.CreateCatalogMangaAsync(BuildRequest());
            if (saved is null)
            {
                messageSeverity = Severity.Error;
                message = IsEditMode ? "Could not save catalog metadata." : "Catalog registration failed. Admin permissions are required.";
                return;
            }

            messageSeverity = Severity.Success;
            message = IsEditMode ? $"Saved {saved.Title}." : $"Added {saved.Title}.";
            await OnSaved.InvokeAsync(saved);
            Reset();
            await OpenChanged.InvokeAsync(false);
        }
        finally
        {
            isSaving = false;
        }
    }

    private MangaEntryRequest BuildRequest()
    {
        var localSeriesId = Guid.TryParse(localSeriesIdText, out var parsed) ? parsed : (Guid?)null;
        return new MangaEntryRequest(
            title,
            authors,
            category,
            description,
            coverUrl,
            openLibraryKey,
            firstPublishYear,
            "",
            mangaDexId,
            localSeriesId,
            "",
            metadataSource,
            myAnimeListId,
            mediaType,
            publishingStatus,
            chapterCount,
            volumeCount,
            mangaUpdatesId,
            fallbackReaderUrl,
            readerPreference);
    }

    private async Task Close()
    {
        if (isSaving)
        {
            return;
        }

        Reset();
        await OpenChanged.InvokeAsync(false);
    }

    private void Reset()
    {
        title = "";
        authors = "";
        category = "";
        description = "";
        coverUrl = "";
        metadataSource = "";
        myAnimeListId = "";
        openLibraryKey = "";
        firstPublishYear = null;
        mediaType = "";
        publishingStatus = "";
        chapterCount = null;
        volumeCount = null;
        mangaDexId = "";
        fallbackReaderUrl = "";
        readerPreference = "mangahub";
        mangaUpdatesId = "";
        localSeriesIdText = "";
        message = "";
        metadataMessage = "";
        metadataSearchVersion++;
        isMatchingMangaDex = false;
        isSaving = false;
        metadataResults = [];
        loadedEntryId = null;
    }

    private void LoadEntry(CatalogMangaResponse entry)
    {
        loadedEntryId = entry.Id;
        title = entry.Title;
        authors = entry.Authors;
        category = entry.Category;
        description = entry.Description;
        coverUrl = entry.CoverUrl;
        metadataSource = entry.MetadataSource;
        myAnimeListId = entry.MyAnimeListId;
        openLibraryKey = entry.OpenLibraryKey;
        firstPublishYear = entry.FirstPublishYear;
        mediaType = entry.MediaType;
        publishingStatus = entry.PublishingStatus;
        chapterCount = entry.ChapterCount;
        volumeCount = entry.VolumeCount;
        mangaDexId = entry.MangaDexId;
        fallbackReaderUrl = entry.FallbackReaderUrl;
        readerPreference = entry.ReaderPreference;
        mangaUpdatesId = entry.MangaUpdatesId;
        localSeriesIdText = entry.LocalSeriesId?.ToString() ?? "";
        message = "";
        metadataMessage = "";
        metadataResults = [];
        metadataSearchVersion++;
    }

    private async Task MatchMangaUpdatesAsync()
    {
        try
        {
            var match = await MetadataApi.FindMangaUpdatesMatchAsync(title, mediaType, firstPublishYear);
            if (match is not null)
            {
                mangaUpdatesId = match.Id;
                messageSeverity = Severity.Success;
                message = $"Filled the form from {title} and linked MangaUpdates: {match.Title}.";
            }
        }
        catch
        {
            // The server repeats this lookup when saving, so a transient preview failure is harmless.
        }
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
