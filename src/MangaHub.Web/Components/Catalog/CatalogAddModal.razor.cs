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
    [Parameter] public List<SeriesResponse> LocalSeries { get; set; } = [];
    [Parameter] public EventCallback OnSaved { get; set; }

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
    private string mangaDexUrl = "";
    private string fallbackReaderUrl = "";
    private string mangaUpdatesId = "";
    private string localSeriesIdText = "";
    private string message = "";
    private Severity messageSeverity = Severity.Info;
    private bool showMetadata;
    private string metadataQuery = "";
    private string metadataMessage = "";
    private Severity metadataSeverity = Severity.Info;
    private bool isSearchingMetadata;
    private bool isMatchingMangaDex;
    private bool isSaving;
    private bool includeOpenLibrary;
    private List<MetadataResult> metadataResults = [];

    private void ToggleMetadata()
    {
        showMetadata = !showMetadata;
        if (showMetadata && string.IsNullOrWhiteSpace(metadataQuery))
        {
            metadataQuery = title;
        }
    }

    private async Task SearchMetadata(bool loadOpenLibrary = false)
    {
        if (string.IsNullOrWhiteSpace(metadataQuery))
        {
            metadataResults = [];
            metadataSeverity = Severity.Info;
            metadataMessage = "Type a title before searching metadata.";
            return;
        }

        includeOpenLibrary = includeOpenLibrary || loadOpenLibrary;
        isSearchingMetadata = true;
        try
        {
            metadataResults = await MetadataApi.SearchAsync(metadataQuery, includeOpenLibrary);
            metadataSeverity = metadataResults.Count == 0 ? Severity.Warning : Severity.Success;
            metadataMessage = metadataResults.Count == 0
                ? "No metadata matches found."
                : $"Found {metadataResults.Count} metadata matches.";
        }
        catch
        {
            metadataResults = [];
            metadataSeverity = Severity.Error;
            metadataMessage = "Metadata search failed.";
        }
        finally
        {
            isSearchingMetadata = false;
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
        if (string.IsNullOrWhiteSpace(mangaUpdatesId))
        {
            await MatchMangaUpdatesAsync();
        }
        if (!string.Equals(item.Source, "myanimelist", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(item.MyAnimeListId)
            || !string.IsNullOrWhiteSpace(mangaDexUrl))
        {
            metadataSeverity = Severity.Success;
            metadataMessage = $"Filled the form from {item.Title}.";
            return;
        }

        isMatchingMangaDex = true;
        metadataSeverity = Severity.Info;
        metadataMessage = "Looking for the matching MangaDex title...";
        try
        {
            var match = await MetadataApi.FindMangaDexMatchAsync(item.MyAnimeListId, item.Title);
            if (match is null)
            {
                metadataSeverity = Severity.Success;
                metadataMessage = $"Filled the form from {item.Title}. No MangaDex match was found.";
                return;
            }

            mangaDexUrl = $"https://mangadex.org/title/{match.Id}";
            metadataSeverity = Severity.Success;
            metadataMessage = $"Filled the form from {item.Title} and linked MangaDex: {match.Title}.";
        }
        catch
        {
            metadataSeverity = Severity.Warning;
            metadataMessage = $"Filled the form from {item.Title}, but MangaDex could not be checked.";
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

        isSaving = true;
        messageSeverity = Severity.Info;
        message = "Adding catalog manga...";
        try
        {
            var created = await CatalogApi.CreateCatalogMangaAsync(BuildRequest());
            if (created is null)
            {
                messageSeverity = Severity.Error;
                message = "Catalog registration failed. Admin permissions are required.";
                return;
            }

            messageSeverity = Severity.Success;
            message = $"Added {created.Title}.";
            await OnSaved.InvokeAsync();
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
            mangaDexUrl,
            localSeriesId,
            "",
            metadataSource,
            myAnimeListId,
            mediaType,
            publishingStatus,
            chapterCount,
            volumeCount,
            mangaUpdatesId,
            fallbackReaderUrl);
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
        mangaDexUrl = "";
        fallbackReaderUrl = "";
        mangaUpdatesId = "";
        localSeriesIdText = "";
        message = "";
        showMetadata = false;
        metadataQuery = "";
        metadataMessage = "";
        includeOpenLibrary = false;
        isMatchingMangaDex = false;
        isSaving = false;
        metadataResults = [];
    }

    private async Task MatchMangaUpdatesAsync()
    {
        try
        {
            var match = await MetadataApi.FindMangaUpdatesMatchAsync(title, mediaType, firstPublishYear);
            if (match is not null)
            {
                mangaUpdatesId = match.Id;
                metadataSeverity = Severity.Success;
                metadataMessage = $"Filled the form from {title} and linked MangaUpdates: {match.Title}.";
            }
        }
        catch
        {
            // The server repeats this lookup when saving, so a transient preview failure is harmless.
        }
    }
}
