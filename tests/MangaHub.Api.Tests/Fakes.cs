using MangaHub.Core.Services;

namespace MangaHub.Api.Tests;

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string password, string storedHash) =>
        storedHash == Hash(password);
}

internal sealed class FakeSessionTokenService : ISessionTokenService
{
    public string CreateToken(Guid userId, string username) => $"token:{userId}:{username}";

    public Guid? ReadUserId(string token)
    {
        var parts = token.Split(':');
        return parts.Length >= 2 && Guid.TryParse(parts[1], out var userId) ? userId : null;
    }
}

internal sealed class FakeArchiveReader : IArchiveReader
{
    public List<string> RequestedPaths { get; } = [];

    public int CountPages(string archivePath) => 1;

    public Task<ArchivePage?> ReadPageAsync(string archivePath, int pageIndex, CancellationToken cancellationToken)
    {
        RequestedPaths.Add(archivePath);
        return Task.FromResult<ArchivePage?>(new ArchivePage($"page-{pageIndex}.jpg", "image/jpeg", [1, 2, 3]));
    }
}
