using System.Collections.Concurrent;
using System.Net;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.RemoteJobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Tests;

public sealed class RemoteRequestSchedulerTests
{
    [Fact]
    public async Task Scheduler_RunsHigherPriorityWorkFirst()
    {
        using var scheduler = CreateScheduler();
        var order = new ConcurrentQueue<string>();
        var low = scheduler.EnqueueAsync(
            RemoteProvider.MangaDexApi,
            RemoteJobPriority.Backfill,
            _ => CompleteAsync("low", order),
            CancellationToken.None);
        var high = scheduler.EnqueueAsync(
            RemoteProvider.MangaDexApi,
            RemoteJobPriority.UserBlocking,
            _ => CompleteAsync("high", order),
            CancellationToken.None);

        await scheduler.StartAsync(CancellationToken.None);
        await Task.WhenAll(low, high);
        await scheduler.StopAsync(CancellationToken.None);

        Assert.Equal(["high", "low"], order);
    }

    [Fact]
    public async Task SchedulingHandler_ObservesRetryAfterForFollowingRequests()
    {
        using var scheduler = CreateScheduler();
        await scheduler.StartAsync(CancellationToken.None);
        var inner = new RateLimitedHandler();
        var handler = new RemoteRequestSchedulingHandler(
            RemoteProvider.MangaDexApi,
            scheduler,
            new RemoteJobPriorityContext())
        {
            InnerHandler = inner
        };
        using var client = new HttpMessageInvoker(handler);

        using var first = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.mangadex.org/first"), CancellationToken.None);
        using var second = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api.mangadex.org/second"), CancellationToken.None);
        await scheduler.StopAsync(CancellationToken.None);

        Assert.Equal(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(inner.SecondRequestAt - inner.FirstRequestAt >= TimeSpan.FromMilliseconds(150));
    }

    private static RemoteRequestScheduler CreateScheduler()
    {
        var settings = new MangaHubOptions
        {
            RemoteRequests = new RemoteRequestLimitsOptions
            {
                MangaDexApi = new RemoteProviderLimitOptions(100, 1)
            }
        };
        return new RemoteRequestScheduler(
            Options.Create(settings),
            NullLogger<RemoteRequestScheduler>.Instance);
    }

    private static Task<HttpResponseMessage> CompleteAsync(string name, ConcurrentQueue<string> order)
    {
        order.Enqueue(name);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class RateLimitedHandler : HttpMessageHandler
    {
        private int requestCount;
        public DateTimeOffset FirstRequestAt { get; private set; }
        public DateTimeOffset SecondRequestAt { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestNumber = Interlocked.Increment(ref requestCount);
            if (requestNumber == 1)
            {
                FirstRequestAt = DateTimeOffset.UtcNow;
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(200));
                return Task.FromResult(response);
            }

            SecondRequestAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
