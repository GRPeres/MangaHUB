namespace MangaHub.Core.Models;

public sealed class MangaUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Username { get; set; }
    public string PasswordHash { get; set; } = "";
    public string Email { get; set; } = "";
    public string PendingEmail { get; set; } = "";
    public DateTimeOffset? EmailConfirmedAt { get; set; }
    public DateTimeOffset? SessionInvalidBefore { get; set; }
    public string GoogleSubject { get; set; } = "";
    public string Role { get; set; } = "user";
    public string PreferredLanguage { get; set; } = "en";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
