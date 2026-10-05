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

    public static bool Verify(string? password, string? passwordHash)
    {
        // Passwordless accounts must use the OTP flow; a missing hash must
        // never make password login succeed for an arbitrary password.
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
            return false;

        var parts = passwordHash.Split(':');
        if (parts.Length != 2)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var storedHash = Convert.FromBase64String(parts[1]);
            if (salt.Length != SaltSize || storedHash.Length != HashSize)
                return false;

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
        catch (FormatException)
        {
            return false;
        }
    }
}