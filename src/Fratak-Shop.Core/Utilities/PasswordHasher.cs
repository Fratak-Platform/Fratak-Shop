using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Fratak_Shop.Core.Utilities;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private const int Iterations = 4;
    private const int MemorySize = 65536;
    private const int Parallelism = 4;

    public static string Hash(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return "";

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            Iterations = Iterations,
            MemorySize = MemorySize,
            DegreeOfParallelism = Parallelism
        };

        var hash = argon2.GetBytes(HashSize);

        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Constant-time verification of an Argon2id hash. An empty hash (account without a
    /// password, e.g. OTP-only users) never matches — callers that allow setting a first
    /// password must check for that case explicitly.
    /// </summary>
    public static bool Verify(string password, string? passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || string.IsNullOrEmpty(password))
            return false;

        var parts = passwordHash.Split(':');
        if (parts.Length != 2)
            return false;

        byte[] salt;
        byte[] storedHash;
        try
        {
            salt = Convert.FromBase64String(parts[0]);
            storedHash = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            Iterations = Iterations,
            MemorySize = MemorySize,
            DegreeOfParallelism = Parallelism
        };

        var computedHash = argon2.GetBytes(HashSize);

        return CryptographicOperations.FixedTimeEquals(storedHash, computedHash);
    }

    /// <summary>Random value that changes whenever the credentials or permissions of a user change.</summary>
    public static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}
