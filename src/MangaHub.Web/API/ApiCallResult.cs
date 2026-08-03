namespace MangaHub.Web.API;

public sealed record ApiCallResult<T>(T? Value, int StatusCode, string Error)
{
    public bool Success => StatusCode is >= 200 and < 300 && Value is not null;
}
