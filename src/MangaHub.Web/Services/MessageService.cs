namespace MangaHub.Web.Services;

public enum MessageLevel
{
    Success,
    Warning,
    Error,
    Info
}

public sealed record AppMessage(MessageLevel Level, string Title, string Message);

public sealed class MessageService
{
    private readonly Queue<AppMessage> queue = [];

    public event Action? Changed;
    public AppMessage? Current { get; private set; }

    public void Show(MessageLevel level, string message, string? title = null)
    {
        queue.Enqueue(new AppMessage(level, title ?? DefaultTitle(level), message));
        ShowNext();
    }

    public void Success(string message, string? title = null) => Show(MessageLevel.Success, message, title);
    public void Warning(string message, string? title = null) => Show(MessageLevel.Warning, message, title);
    public void Error(string message, string? title = null) => Show(MessageLevel.Error, message, title);
    public void Info(string message, string? title = null) => Show(MessageLevel.Info, message, title);

    public void Dismiss()
    {
        Current = queue.Count == 0 ? null : queue.Dequeue();
        Changed?.Invoke();
    }

    private void ShowNext()
    {
        if (Current is not null || queue.Count == 0)
        {
            return;
        }

        Current = queue.Dequeue();
        Changed?.Invoke();
    }

    private static string DefaultTitle(MessageLevel level) => level switch
    {
        MessageLevel.Success => "Done",
        MessageLevel.Warning => "Heads up",
        MessageLevel.Error => "Something went wrong",
        _ => "MangaHub"
    };
}
