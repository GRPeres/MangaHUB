namespace MangaHub.Web.Services;

public sealed class ShelfApiService(ApiHttpClient api)
{
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
}
