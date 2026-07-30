using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MangaHub.Web.API.DTOs;
using MangaHub.Web.API.Services;
using MudBlazor;

namespace MangaHub.Web.Components.Catalog;

public partial class CatalogCacheModal
{
    [Inject] private CatalogApiService CatalogApi { get; set; } = default!;

    [Parameter] public CatalogMangaResponse? Entry { get; set; }
    [Parameter] public string Language { get; set; } = "en";
    [Parameter] public EventCallback OnClosed { get; set; }
    [Parameter] public EventCallback OnChanged { get; set; }

    private Guid? loadedEntryId;
    private string loadedLanguage = "";
    private MangaDexCacheResponse? cache;
    private IBrowserFile? selectedFile;
    private string chapterToDownload = "";
    private string manualChapterNumber = "";
    private string manualChapterTitle = "";
    private string message = "";
    private Severity messageSeverity = Severity.Info;
    private bool isLoading;
    private bool isWorking;
    private CachedMangaDexChapterResponse? editingChapter;
    private string editChapterNumber = "";
    private string editLanguage = "";
    private string editTitle = "";
    private string CachedCountLabel => cache is null ? "Cached chapters" : $"Cached chapters ({cache.Chapters.Count})";

    protected override async Task OnParametersSetAsync()
    {
        if (Entry is null || (loadedEntryId == Entry.Id && string.Equals(loadedLanguage, Language, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        loadedEntryId = Entry.Id;
        loadedLanguage = Language;
        cache = null;
        selectedFile = null;
        chapterToDownload = "";
        manualChapterNumber = "";
        manualChapterTitle = "";
        message = "";
        await LoadCache();
    }

    private async Task LoadCache()
    {
        if (Entry is null)
        {
            return;
        }

        isLoading = true;
        try
        {
            cache = await CatalogApi.GetMangaDexCacheAsync(Entry.Id, Language);
            if (cache is null)
            {
                messageSeverity = Severity.Warning;
                message = "This catalog entry does not have a valid MangaDex link.";
            }
        }
        finally
        {
            isLoading = false;
        }
    }

    private void SelectFile(InputFileChangeEventArgs args) => selectedFile = args.File;

    private async Task DownloadChapter()
    {
        if (Entry is null || string.IsNullOrWhiteSpace(chapterToDownload))
        {
            SetMessage(Severity.Warning, "Enter a MangaDex chapter number first.");
            return;
        }

        isWorking = true;
        try
        {
            cache = await CatalogApi.DownloadMangaDexChapterAsync(Entry.Id, chapterToDownload.Trim(), Language);
            SetMessage(cache is null ? Severity.Error : Severity.Success, cache is null ? "MangaDex could not provide that chapter." : $"Cached chapter {chapterToDownload}.");
            if (cache is not null)
            {
                await OnChanged.InvokeAsync();
            }
        }
        finally
        {
            isWorking = false;
        }
    }

    private async Task ImportChapter()
    {
        if (Entry is null || selectedFile is null || string.IsNullOrWhiteSpace(manualChapterNumber))
        {
            SetMessage(Severity.Warning, "Choose a .cbz file and enter its chapter number.");
            return;
        }

        isWorking = true;
        try
        {
            cache = await CatalogApi.ImportMangaDexChapterAsync(Entry.Id, manualChapterNumber.Trim(), manualChapterTitle.Trim(), Language, selectedFile);
            if (cache is null)
            {
                SetMessage(Severity.Error, "The CBZ could not be imported. Check that it is a valid non-empty .cbz archive.");
                return;
            }

            selectedFile = null;
            manualChapterTitle = "";
            SetMessage(Severity.Success, $"Imported chapter {manualChapterNumber} into the cache.");
            await OnChanged.InvokeAsync();
        }
        finally
        {
            isWorking = false;
        }
    }

    private async Task DeleteChapter(CachedMangaDexChapterResponse chapter)
    {
        if (Entry is null)
        {
            return;
        }

        isWorking = true;
        try
        {
            var deleted = await CatalogApi.DeleteMangaDexChapterAsync(Entry.Id, chapter.Id);
            if (!deleted)
            {
                SetMessage(Severity.Error, "That cached chapter could not be deleted.");
                return;
            }

            cache = await CatalogApi.GetMangaDexCacheAsync(Entry.Id, Language);
            SetMessage(Severity.Success, $"Removed cached chapter {chapter.ChapterNumber}.");
            await OnChanged.InvokeAsync();
        }
        finally
        {
            isWorking = false;
        }
    }

    private void BeginEdit(CachedMangaDexChapterResponse chapter)
    {
        editingChapter = chapter;
        editChapterNumber = chapter.ChapterNumber;
        editLanguage = chapter.Language;
        editTitle = chapter.Title;
    }

    private void CancelEdit() => editingChapter = null;

    private async Task SaveEdit()
    {
        if (Entry is null || editingChapter is null || string.IsNullOrWhiteSpace(editChapterNumber)) return;
        isWorking = true;
        try
        {
            cache = await CatalogApi.UpdateMangaDexChapterAsync(Entry.Id, editingChapter.Id, new UpdateCachedMangaDexChapterRequest(editChapterNumber, editLanguage, editTitle));
            editingChapter = null;
            SetMessage(cache is null ? Severity.Error : Severity.Success, cache is null ? "Could not update the cached chapter." : "Cached chapter updated.");
            await OnChanged.InvokeAsync();
        }
        finally { isWorking = false; }
    }

    private async Task Close()
    {
        loadedEntryId = null;
        await OnClosed.InvokeAsync();
    }

    private void SetMessage(Severity severity, string value)
    {
        messageSeverity = severity;
        message = value;
    }
}
