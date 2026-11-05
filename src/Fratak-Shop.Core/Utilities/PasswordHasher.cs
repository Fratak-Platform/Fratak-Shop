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

    // دور نوزدهم (A10): هر محاسبهٔ Argon2 حدود ۶۴ مگابایت حافظه می‌خواهد (MemorySize بالا). چند ورود/ثبت‌نام هم‌زمان (یا حملهٔ
    // ورود انبوه) بدون صف چند صد مگ حافظه می‌گرفت و روی هاست کم‌حافظه پروسه را می‌کشت. حداکثر ۲ محاسبهٔ هم‌زمان؛ بقیه چند
    // دهم ثانیه صبر می‌کنند. پارامترها عوض نشده‌اند (هش‌های قبلی معتبر می‌مانند).
    private static readonly SemaphoreSlim Gate = new(2, 2);

    /// <summary>محاسبهٔ هش با رعایت صف بالا. مثال: <c>Compute(argon2)</c> → ۳۲ بایت.</summary>
    private static byte[] Compute(Argon2id argon2)
    {
        Gate.Wait();
        try
        {
            return argon2.GetBytes(HashSize);
        }
        finally
        {
            Gate.Release();
        }
    }

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

        var hash = Compute(argon2);

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

        var computedHash = Compute(argon2);

        return CryptographicOperations.FixedTimeEquals(storedHash, computedHash);
    }

    /// <summary>Random value that changes whenever the credentials or permissions of a user change.</summary>
    public static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}
