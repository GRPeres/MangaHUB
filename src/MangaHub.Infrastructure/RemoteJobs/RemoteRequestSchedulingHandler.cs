using System.Net;

namespace MangaHub.Infrastructure.RemoteJobs;

public sealed class RemoteRequestSchedulingHandler(
    RemoteProvider provider,
    IRemoteRequestScheduler scheduler,
    RemoteJobPriorityContext priorityContext) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await scheduler.EnqueueAsync(
            provider,
            priorityContext.Current,
            token => base.SendAsync(request, token),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            scheduler.Defer(provider, GetRetryDelay(response));
        }

        return response;
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date && date > DateTimeOffset.UtcNow)
        {
            return date - DateTimeOffset.UtcNow;
        }

        return TimeSpan.FromSeconds(5);
    }
}
