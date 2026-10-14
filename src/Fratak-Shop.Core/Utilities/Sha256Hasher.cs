using System.Security.Cryptography;
using System.Text;

namespace Fratak_Shop.Core.Utilities;


public class Sha256Hasher
{
    public static string Hash(string? inputValue)
    {
        if (string.IsNullOrWhiteSpace(inputValue))
            return "";

        var originalBytes = Encoding.Default.GetBytes(inputValue);
        var encodedBytes = SHA256.HashData(originalBytes);
        return Convert.ToBase64String(encodedBytes);
    }
    public static bool IsCompare(string hashText, string? rawText)
    {
        var hash = Hash(rawText);
        return hashText == hash;
    }
}
public class VerificationCodeHasher
{
    private static readonly byte[] SecretKey =
        Encoding.UTF8.GetBytes("VERIFICATION_CODE_SECRET");//TODO 

    public static string Hash(string inputValue)
    {
        if (string.IsNullOrWhiteSpace(inputValue))
            return "";

        var messageBytes = Encoding.UTF8.GetBytes(inputValue);

        using var hmac = new HMACSHA256(SecretKey);
        var hashBytes = hmac.ComputeHash(messageBytes);

        return Convert.ToBase64String(hashBytes);
    }

    public static bool IsCompare(string hashText, string rawText)
    {
        if (string.IsNullOrEmpty(hashText) || string.IsNullOrWhiteSpace(rawText))
            return false;

        var computedHash = Hash(rawText);

        var hashBytes = Convert.FromBase64String(hashText);
        var computedBytes = Convert.FromBase64String(computedHash);

        return CryptographicOperations.FixedTimeEquals(hashBytes, computedBytes);
    }
}