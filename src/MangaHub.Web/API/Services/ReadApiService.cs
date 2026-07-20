namespace MangaHub.Web.API.Services;

public sealed class ReadApiService(ApiHttpClient api)
{
    public string GetPageUrl(Guid chapterId, int pageIndex) =>
        api.GetAbsoluteUrl($"/api/read/{chapterId}/pages/{pageIndex}");
}

