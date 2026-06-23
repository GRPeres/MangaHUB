using System.IO.Compression;
using MangaHub.Infrastructure;
using MangaHub.Infrastructure.Local;
using MangaHub.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace MangaHub.Api.Tests;

public sealed class SecurityAndArchiveTests
{
    [Fact]
    public void JwtSessionTokenService_RoundTripsUserIdAndRejectsWrongSecret()
    {
        var userId = Guid.NewGuid();
        var service = TokenService("this-is-a-long-test-secret-for-hs256");
        var wrongSecret = TokenService("this-is-a-different-long-test-secret");

        var token = service.CreateToken(userId, "delta");

        Assert.Equal(userId, service.ReadUserId(token));
        Assert.Null(wrongSecret.ReadUserId(token));
    }

    [Fact]
    public void Argon2idPasswordHasher_VerifiesCorrectPasswordOnly()
    {
        var hasher = new Argon2idPasswordHasher();

        var hash = hasher.Hash("secret");

        Assert.True(hasher.Verify("secret", hash));
        Assert.False(hasher.Verify("wrong", hash));
        Assert.False(hasher.Verify("secret", "not-a-valid-hash"));
    }

    [Fact]
    public async Task CbzArchiveReader_CountsAndReadsImagesInSortedOrder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.cbz");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            AddEntry(zip, "002.png", [2]);
            AddEntry(zip, "notes.txt", [9]);
            AddEntry(zip, "001.jpg", [1]);
        }

        var reader = new CbzArchiveReader();

        Assert.Equal(2, reader.CountPages(path));
        var page = await reader.ReadPageAsync(path, 0, CancellationToken.None);
        var missing = await reader.ReadPageAsync(path, 99, CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal("001.jpg", page.FileName);
        Assert.Equal("image/jpeg", page.ContentType);
        Assert.Equal([1], page.Bytes);
        Assert.Null(missing);
    }

    private static JwtSessionTokenService TokenService(string secret) =>
        new(Options.Create(new MangaHubOptions { JwtSecret = secret }));

    private static void AddEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(bytes);
    }
}
