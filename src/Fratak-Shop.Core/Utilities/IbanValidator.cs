using System.Numerics;

namespace Fratak_Shop.Core.Utilities;

/// <summary>Validation of Iranian IBAN (شبا) numbers: IR + 24 digits with a valid ISO 7064 checksum.</summary>
public static class IbanValidator
{
    /// <summary>Normalizes user input (spaces, dashes, Persian digits, missing IR prefix) to "IR" + 24 digits.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value.Trim().ToUpperInvariant()
            .Select(ch => ch switch
            {
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                _ => ch,
            })
            .Where(char.IsLetterOrDigit)
            .ToArray();

        var normalized = new string(chars);
        if (normalized.Length == 24 && normalized.All(char.IsDigit))
            normalized = "IR" + normalized;

        return normalized;
    }

    public static bool IsValid(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban) || iban.Length != 26 || !iban.StartsWith("IR", StringComparison.Ordinal))
            return false;

        if (!iban[2..].All(char.IsDigit))
            return false;

        // Move the first four characters to the end and replace letters (I=18, R=27).
        var rearranged = iban[4..] + "1827" + iban[2..4];
        return BigInteger.Parse(rearranged) % 97 == 1;
    }
}
