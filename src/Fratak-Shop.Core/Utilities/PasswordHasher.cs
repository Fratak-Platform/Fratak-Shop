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

    public static bool Verify(string password, string? passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) // در این سیستم امکان اینکه کاربر پسووردی برای خود تنظیم نکرده باشد هست
            return true;                                      // گذاشتن این شرط یعنی اگر پسووردی که در دیتابیس ذخیره شده نال هست یعنی کاربر
                                                              // کلمه عبوری ندارد وباید بگذاریم پسوورد خود را بدون وارد کردن پسوورد فعلی تنظیم کند
        var parts = passwordHash.Split(':');

        if (parts.Length != 2)
            return false;

        var salt = Convert.FromBase64String(parts[0]);
        var storedHash = Convert.FromBase64String(parts[1]);

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
}