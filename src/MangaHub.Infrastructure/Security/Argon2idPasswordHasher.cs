using System.Security.Cryptography;
using Konscious.Security.Cryptography;
using MangaHub.Core.Services;

namespace MangaHub.Infrastructure.Security;

public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Hash(password, salt);
        return $"argon2id$v=1$s={Convert.ToBase64String(salt)}$h={Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "argon2id")
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2][2..]);
        var expected = Convert.FromBase64String(parts[3][2..]);
        var actual = Hash(password, salt);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Hash(string password, byte[] salt)
    {
        var argon2 = new Argon2id(System.Text.Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = 2,
            Iterations = 3,
            MemorySize = 64 * 1024
        };

        return argon2.GetBytes(32);
    }
}

