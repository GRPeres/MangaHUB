using MudBlazor;

namespace MangaHub.Web.Services;

public enum MessageLevel
{
    Success,
    Warning,
    Error,
    Info
}

public sealed class MessageService(ISnackbar snackbar)
{
    public void Show(MessageLevel level, string message, string? title = null)
    {
        var severity = level switch
        {
            MessageLevel.Success => Severity.Success,
            MessageLevel.Warning => Severity.Warning,
            MessageLevel.Error => Severity.Error,
            _ => Severity.Info
        };
        var text = string.IsNullOrWhiteSpace(title) ? message : $"{title}: {message}";
        snackbar.Add(text, severity, options =>
        {
            options.ShowCloseIcon = true;
            options.VisibleStateDuration = level is MessageLevel.Warning or MessageLevel.Error ? 9000 : 5000;
        });
    }

    public void Success(string message, string? title = null) => Show(MessageLevel.Success, message, title);
    public void Warning(string message, string? title = null) => Show(MessageLevel.Warning, message, title);
    public void Error(string message, string? title = null) => Show(MessageLevel.Error, message, title);
    public void Info(string message, string? title = null) => Show(MessageLevel.Info, message, title);

}
