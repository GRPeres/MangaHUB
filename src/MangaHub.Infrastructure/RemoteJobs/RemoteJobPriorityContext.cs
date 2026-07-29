namespace MangaHub.Infrastructure.RemoteJobs;

public sealed class RemoteJobPriorityContext
{
    private readonly AsyncLocal<RemoteJobPriority?> current = new();

    public RemoteJobPriority Current => current.Value ?? RemoteJobPriority.Interactive;

    public IDisposable Push(RemoteJobPriority priority)
    {
        var previous = current.Value;
        current.Value = priority;
        return new PriorityScope(this, previous);
    }

    private sealed class PriorityScope(RemoteJobPriorityContext owner, RemoteJobPriority? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            owner.current.Value = previous;
            disposed = true;
        }
    }
}
