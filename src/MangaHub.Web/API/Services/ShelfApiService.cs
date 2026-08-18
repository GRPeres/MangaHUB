namespace MangaHub.Web.API.Services;

public sealed class ShelfApiService(ApiHttpClient api)
{
    public string GetExportCsvUrl(string? section = null) => api.GetAbsoluteUrl(ExportUrl("csv", section));
    public string GetExportPdfUrl(string? section = null) => api.GetAbsoluteUrl(ExportUrl("pdf", section));

    public async Task<MangaEntryResponse?> AddToShelfAsync(AddToShelfRequest request) =>
        await api.SendAsync<AddToShelfRequest, MangaEntryResponse>(HttpMethod.Post, "/api/shelf", request);

    public async Task<MangaEntryResponse?> UpdateShelfAsync(Guid entryId, AddToShelfRequest request, Guid? userId = null)
    {
        var query = userId is null ? "" : $"?userId={Uri.EscapeDataString(userId.Value.ToString())}";
        return await api.SendAsync<AddToShelfRequest, MangaEntryResponse>(HttpMethod.Put, $"/api/shelf/{entryId}{query}", request);
    }

    public async Task<bool> RemoveShelfAsync(Guid entryId, Guid? userId = null)
    {
        var query = userId is null ? "" : $"?userId={Uri.EscapeDataString(userId.Value.ToString())}";
        return await api.DeleteAsync($"/api/shelf/{entryId}{query}");
    }

    public async Task<ShelfImportResponse?> ImportShelfAsync(ShelfImportRequest request) =>
        await api.SendAsync<ShelfImportRequest, ShelfImportResponse>(HttpMethod.Post, "/api/shelf/import", request);

    private static string ExportUrl(string format, string? section) =>
        string.IsNullOrWhiteSpace(section) || string.Equals(section, "all", StringComparison.OrdinalIgnoreCase)
            ? $"/api/shelf/export/{format}"
            : $"/api/shelf/export/{format}?section={Uri.EscapeDataString(section)}";
}

