namespace MangaHub.Web.API.Services;

public sealed class ReadApiService(ApiHttpClient api)
{
    public string GetPageUrl(Guid chapterId, int pageIndex, string targetLanguage, string version) =>
        api.GetAbsoluteUrl(
            $"/api/read/{chapterId}/pages/{pageIndex}?targetLanguage={Uri.EscapeDataString(targetLanguage)}&v={Uri.EscapeDataString(version)}");
}

