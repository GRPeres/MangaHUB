using MangaHub.Web.Services;

namespace MangaHub.Web.API.Services;

public sealed class ShelfApiService(ApiHttpClient api, AppRefreshService refreshes)
{
    public string GetExportCsvUrl(string? section = null) => api.GetAbsoluteUrl(ExportUrl("csv", section));
    public string GetExportPdfUrl(string? section = null) => api.GetAbsoluteUrl(ExportUrl("pdf", section));

    public async Task<MangaEntryResponse?> AddToShelfAsync(AddToShelfRequest request)
    {
        var created = await api.SendAsync<AddToShelfRequest, MangaEntryResponse>(HttpMethod.Post, "/api/shelf", request);
        NotifyShelfMutation(created is not null);
        return created;
    }

    public async Task<MangaEntryResponse?> UpdateShelfAsync(Guid entryId, AddToShelfRequest request, Guid? userId = null)
    {
        var query = userId is null ? "" : $"?userId={Uri.EscapeDataString(userId.Value.ToString())}";
        var updated = await api.SendAsync<AddToShelfRequest, MangaEntryResponse>(HttpMethod.Put, $"/api/shelf/{entryId}{query}", request);
        NotifyShelfMutation(updated is not null);
        return updated;
    }

    public async Task<bool> RemoveShelfAsync(Guid entryId, Guid? userId = null)
    {
        var query = userId is null ? "" : $"?userId={Uri.EscapeDataString(userId.Value.ToString())}";
        var removed = await api.DeleteAsync($"/api/shelf/{entryId}{query}");
        NotifyShelfMutation(removed);
        return removed;
    }

    public async Task<List<ExternalReaderCheckInResponse>> GetPendingExternalReaderCheckInsAsync() =>
        await api.GetAsync<List<ExternalReaderCheckInResponse>>("/api/shelf/external-reader/check-ins") ?? [];

    public async Task<bool> RecordExternalReaderOpenedAsync(Guid entryId)
    {
        var recorded = await api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/shelf/{entryId}/external-reader/opened", new { });
        NotifyShelfMutation(recorded);
        return recorded;
    }

    public async Task<bool> VerifyExternalReaderCheckAsync(Guid entryId)
    {
        var verified = await api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/shelf/{entryId}/external-reader/verified", new { });
        NotifyShelfMutation(verified);
        return verified;
    }

    public async Task<bool> UpdateExternalReaderProgressAsync(Guid entryId, string currentChapter, string? latestChapter)
    {
        var updated = await api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/shelf/{entryId}/external-reader/progress", new ExternalReaderProgressRequest(currentChapter, latestChapter));
        NotifyShelfMutation(updated);
        return updated;
    }

    public async Task<bool> DismissExternalReaderCheckAsync(Guid entryId)
    {
        var dismissed = await api.SendWithoutResponseAsync(HttpMethod.Post, $"/api/shelf/{entryId}/external-reader/dismiss", new { });
        NotifyShelfMutation(dismissed);
        return dismissed;
    }

    public async Task<ShelfImportResponse?> ImportShelfAsync(ShelfImportRequest request)
    {
        var result = await api.SendAsync<ShelfImportRequest, ShelfImportResponse>(HttpMethod.Post, "/api/shelf/import", request);
        NotifyShelfMutation(result is not null);
        return result;
    }

    private void NotifyShelfMutation(bool succeeded)
    {
        if (succeeded)
        {
            refreshes.Notify(AppRefreshScope.Shelf | AppRefreshScope.Catalog | AppRefreshScope.Notifications | AppRefreshScope.Analytics);
        }
    }

    private static string ExportUrl(string format, string? section) =>
        string.IsNullOrWhiteSpace(section) || string.Equals(section, "all", StringComparison.OrdinalIgnoreCase)
            ? $"/api/shelf/export/{format}"
            : $"/api/shelf/export/{format}?section={Uri.EscapeDataString(section)}";
}

