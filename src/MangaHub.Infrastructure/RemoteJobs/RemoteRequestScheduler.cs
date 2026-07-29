using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MangaHub.Infrastructure.RemoteJobs;

public interface IRemoteRequestScheduler
{
    Task<HttpResponseMessage> EnqueueAsync(
        RemoteProvider provider,
        RemoteJobPriority priority,
        Func<CancellationToken, Task<HttpResponseMessage>> operation,
        CancellationToken cancellationToken);

    void Defer(RemoteProvider provider, TimeSpan delay);
}

public sealed class RemoteRequestScheduler : IRemoteRequestScheduler, IHostedService, IDisposable
{
    private readonly Dictionary<RemoteProvider, ProviderQueue> queues;
    private readonly ILogger<RemoteRequestScheduler> logger;
    private readonly CancellationTokenSource shutdown = new();
    private readonly List<Task> processors = [];
    private long sequence;

    public RemoteRequestScheduler(
        IOptions<MangaHubOptions> options,
        ILogger<RemoteRequestScheduler> logger)
    {
        this.logger = logger;
        queues = Enum.GetValues<RemoteProvider>()
            .ToDictionary(
                provider => provider,
                provider => new ProviderQueue(options.Value.RemoteRequests.Get(provider)));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var queue in queues.Values)
        {
            for (var index = 0; index < queue.MaxConcurrency; index++)
            {
                processors.Add(ProcessQueueAsync(queue, shutdown.Token));
            }
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        shutdown.Cancel();
        foreach (var queue in queues.Values)
        {
            queue.Signal.Release(queue.MaxConcurrency);
        }

        await Task.WhenAll(processors).WaitAsync(cancellationToken);
    }

    public async Task<HttpResponseMessage> EnqueueAsync(
        RemoteProvider provider,
        RemoteJobPriority priority,
        Func<CancellationToken, Task<HttpResponseMessage>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new QueuedRequest(operation, completion, cancellationToken);
        var queue = queues[provider];

        lock (queue.SyncRoot)
        {
            queue.Requests.Enqueue(request, ((int)priority, Interlocked.Increment(ref sequence)));
        }

        queue.Signal.Release();
        return await completion.Task.WaitAsync(cancellationToken);
    }

    public void Defer(RemoteProvider provider, TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        var queue = queues[provider];
        lock (queue.SyncRoot)
        {
            var deferredUntil = DateTimeOffset.UtcNow.Add(delay);
            if (deferredUntil > queue.DeferredUntil)
            {
                queue.DeferredUntil = deferredUntil;
            }
        }

        logger.LogWarning("Remote provider {Provider} paused for {Delay} after rate limiting.", provider, delay);
    }

    public void Dispose()
    {
        shutdown.Dispose();
        foreach (var queue in queues.Values)
        {
            queue.Signal.Dispose();
        }
    }

    private async Task ProcessQueueAsync(ProviderQueue queue, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await queue.Signal.WaitAsync(cancellationToken);
                QueuedRequest? request;
                lock (queue.SyncRoot)
                {
                    request = queue.Requests.Count == 0 ? null : queue.Requests.Dequeue();
                }

                if (request is null)
                {
                    continue;
                }

                if (request.CancellationToken.IsCancellationRequested)
                {
                    request.Completion.TrySetCanceled(request.CancellationToken);
                    continue;
                }

                await WaitForPermitAsync(queue, cancellationToken);
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    request.CancellationToken);
                try
                {
                    request.Completion.TrySetResult(await request.Operation(linkedCancellation.Token));
                }
                catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
                {
                    request.Completion.TrySetCanceled(linkedCancellation.Token);
                }
                catch (Exception exception)
                {
                    request.Completion.TrySetException(exception);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task WaitForPermitAsync(ProviderQueue queue, CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;
            lock (queue.SyncRoot)
            {
                var now = DateTimeOffset.UtcNow;
                var nextPermitAt = queue.NextPermitAt > queue.DeferredUntil
                    ? queue.NextPermitAt
                    : queue.DeferredUntil;
                if (nextPermitAt <= now)
                {
                    queue.NextPermitAt = now.Add(queue.RequestInterval);
                    return;
                }

                delay = nextPermitAt - now;
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    private sealed class ProviderQueue
    {
        public ProviderQueue(RemoteProviderLimitOptions options)
        {
            var requestsPerSecond = Math.Clamp(options.RequestsPerSecond, 0.05, 100);
            RequestInterval = TimeSpan.FromSeconds(1d / requestsPerSecond);
            MaxConcurrency = Math.Clamp(options.MaxConcurrency, 1, 16);
        }

        public object SyncRoot { get; } = new();
        public PriorityQueue<QueuedRequest, (int Priority, long Sequence)> Requests { get; } = new();
        public SemaphoreSlim Signal { get; } = new(0);
        public TimeSpan RequestInterval { get; }
        public int MaxConcurrency { get; }
        public DateTimeOffset NextPermitAt { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset DeferredUntil { get; set; } = DateTimeOffset.MinValue;
    }

    private sealed record QueuedRequest(
        Func<CancellationToken, Task<HttpResponseMessage>> Operation,
        TaskCompletionSource<HttpResponseMessage> Completion,
        CancellationToken CancellationToken);
}
