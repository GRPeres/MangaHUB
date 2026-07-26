namespace MangaHub.Core.Models;

public sealed class MangaUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public string Role { get; set; } = "user";
    public string PreferredLanguage { get; set; } = "en";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
