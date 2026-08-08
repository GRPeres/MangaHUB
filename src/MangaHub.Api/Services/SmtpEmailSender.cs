using System.Net;
using System.Net.Mail;
using MangaHub.Infrastructure;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Services;

public sealed class SmtpEmailSender(IOptions<MangaHubOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private EmailOptions Settings => options.Value.Email;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.SmtpHost)
                                && !string.IsNullOrWhiteSpace(Settings.SmtpUsername)
                                && !string.IsNullOrWhiteSpace(Settings.SmtpPassword)
                                && !string.IsNullOrWhiteSpace(Settings.FromAddress);

    public async Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("Email is not configured.");

        using var client = new SmtpClient(Settings.SmtpHost, Settings.SmtpPort)
        {
            EnableSsl = Settings.UseSsl,
            Credentials = new NetworkCredential(Settings.SmtpUsername, Settings.SmtpPassword)
        };
        using var message = new MailMessage(Settings.FromAddress, recipient, subject, htmlBody) { IsBodyHtml = true };
        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to send account email to {Recipient}", recipient);
            throw;
        }
    }
}
