using System.Security.Cryptography;
using System.Text;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// Gift card codes: 16 random characters from an alphabet without look-alikes (0/O, 1/I/L),
/// shown as XXXX-XXXX-XXXX-XXXX (≈ 80 bits of entropy). Only the SHA-256 hash is searchable
/// in the database, so a leaked table can't be used to redeem cards.
/// </summary>
public static class GiftCardCode
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 16;

    public static string Generate()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

        return Format(new string(chars));
    }

    /// <summary>Upper-case, Persian/Arabic digits → Latin, separators and spaces removed. Null when malformed.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 40)
            return null;

        var builder = new StringBuilder(Length);
        foreach (var raw in input)
        {
            var c = raw switch
            {
                >= '۰' and <= '۹' => (char)('0' + (raw - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (raw - '٠')),
                _ => char.ToUpperInvariant(raw),
            };

            if (c is '-' or ' ' or '\u200c' or '_')
                continue;

            if (Alphabet.IndexOf(c) < 0)
                return null;

            builder.Append(c);
        }

        return builder.Length == Length ? builder.ToString() : null;
    }

    public static string Format(string normalized) =>
        normalized.Length != Length
            ? normalized
            : $"{normalized[..4]}-{normalized[4..8]}-{normalized[8..12]}-{normalized[12..]}";

    public static string Hash(string normalized) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fratak-gift:" + normalized)));

    /// <summary>Public reference shown on receipts (not secret): GC-XXXXXXXX.</summary>
    public static string NewReference()
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return "GC-" + new string(chars);
    }
}
