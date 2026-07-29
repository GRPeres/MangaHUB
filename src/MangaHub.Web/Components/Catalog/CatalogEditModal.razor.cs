using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace MangaHub.Web.Components.Catalog;

public partial class CatalogEditModal
{
    [Inject] private CatalogApiService CatalogApi { get; set; } = default!;
    [Inject] private MetadataApiService MetadataApi { get; set; } = default!;

    [Parameter] public CatalogMangaResponse? Entry { get; set; }
    [Parameter] public List<SeriesResponse> LocalSeries { get; set; } = [];
    [Parameter] public EventCallback<CatalogMangaResponse> OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    private Guid? loadedEntryId;
    private string editTitle = "";
    private string editAuthors = "";
    private string editCategory = "";
    private string editDescription = "";
    private string editCoverUrl = "";
    private string editMetadataSource = "";
    private string editMyAnimeListId = "";
    private string editOpenLibraryKey = "";
    private int? editFirstPublishYear;
    private string editMediaType = "";
    private string editPublishingStatus = "";
    private int? editChapterCount;
    private int? editVolumeCount;
    private string editMangaDexUrl = "";
    private string editFallbackReaderUrl = "";
    private string editMangaUpdatesId = "";
    private string editLocalSeriesIdText = "";
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

    protected override void OnParametersSet()
    {
        if (Entry is null || loadedEntryId == Entry.Id)
        {
            return;
        }

        loadedEntryId = Entry.Id;
        editTitle = Entry.Title;
        editAuthors = Entry.Authors;
        editCategory = Entry.Category;
        editDescription = Entry.Description;
        editCoverUrl = Entry.CoverUrl;
        editMetadataSource = Entry.MetadataSource;
        editMyAnimeListId = Entry.MyAnimeListId;
        editOpenLibraryKey = Entry.OpenLibraryKey;
        editFirstPublishYear = Entry.FirstPublishYear;
        editMediaType = Entry.MediaType;
        editPublishingStatus = Entry.PublishingStatus;
        editChapterCount = Entry.ChapterCount;
        editVolumeCount = Entry.VolumeCount;
        editMangaDexUrl = Entry.MangaDexUrl;
        editFallbackReaderUrl = Entry.FallbackReaderUrl;
        editMangaUpdatesId = Entry.MangaUpdatesId;
        editLocalSeriesIdText = Entry.LocalSeriesId?.ToString() ?? "";
        metadataQuery = Entry.Title;
        metadataResults = [];
        metadataMessage = "";
        includeOpenLibrary = false;
        message = "";
        isSaving = false;
        showMetadata = false;
    }

    private void ToggleMetadata() => showMetadata = !showMetadata;

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
        editTitle = item.Title;
        editAuthors = item.Authors;
        editCategory = item.Category;
        editDescription = item.Description;
        editCoverUrl = item.CoverUrl;
        editMetadataSource = item.Source;
        editMyAnimeListId = item.MyAnimeListId;
        editOpenLibraryKey = item.OpenLibraryKey;
        editFirstPublishYear = item.FirstPublishYear;
        editMediaType = item.MediaType;
        editPublishingStatus = item.PublishingStatus;
        editChapterCount = item.ChapterCount;
        editVolumeCount = item.VolumeCount;
        if (string.IsNullOrWhiteSpace(editMangaUpdatesId))
        {
            await MatchMangaUpdatesAsync();
        }
        if (string.IsNullOrWhiteSpace(editMangaDexUrl)
            && string.Equals(item.Source, "myanimelist", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(item.MyAnimeListId))
        {
            await MatchMangaDexAsync();
        }
        metadataSeverity = Severity.Success;
        metadataMessage = $"Filled the form from {item.Title}.";
    }

    private async Task SaveCatalog()
    {
        if (Entry is null || isSaving)
        {
            return;
        }

        isSaving = true;
        messageSeverity = Severity.Info;
        message = "Saving catalog metadata...";
        try
        {
            var updated = await CatalogApi.UpdateCatalogMangaAsync(Entry.Id, BuildRequest());
            if (updated is null)
            {
                messageSeverity = Severity.Error;
                message = "Could not save catalog metadata.";
                return;
            }

            messageSeverity = Severity.Success;
            message = $"Saved {updated.Title}.";
            await OnSaved.InvokeAsync(updated);
        }
        finally
        {
            isSaving = false;
        }
    }

    private MangaEntryRequest BuildRequest()
    {
        var localSeriesId = Guid.TryParse(editLocalSeriesIdText, out var parsed) ? parsed : (Guid?)null;
        return new MangaEntryRequest(
            editTitle,
            editAuthors,
            editCategory,
            editDescription,
            editCoverUrl,
            editOpenLibraryKey,
            editFirstPublishYear,
            "",
            editMangaDexUrl,
            localSeriesId,
            "",
            editMetadataSource,
            editMyAnimeListId,
            editMediaType,
            editPublishingStatus,
            editChapterCount,
            editVolumeCount,
            editMangaUpdatesId,
            editFallbackReaderUrl);
    }

    private async Task Close()
    {
        if (isSaving)
        {
            return;
        }

        loadedEntryId = null;
        await OnClosed.InvokeAsync();
    }

    private async Task MatchMangaUpdatesAsync()
    {
        try
        {
            var match = await MetadataApi.FindMangaUpdatesMatchAsync(editTitle, editMediaType, editFirstPublishYear);
            if (match is not null)
            {
                editMangaUpdatesId = match.Id;
                metadataSeverity = Severity.Success;
                metadataMessage = $"Filled the form from {editTitle} and linked MangaUpdates: {match.Title}.";
            }
        }
        catch
        {
            // Saving retries the same automatic lookup on the API.
        }
    }

    private async Task MatchMangaDexAsync()
    {
        isMatchingMangaDex = true;
        try
        {
            var match = await MetadataApi.FindMangaDexMatchAsync(editMyAnimeListId, editTitle);
            if (match is not null)
            {
                editMangaDexUrl = $"https://mangadex.org/title/{match.Id}";
            }
        }
        catch
        {
            // The API retries the lookup when catalog metadata is saved.
        }
        finally
        {
            isMatchingMangaDex = false;
        }
    }
}
