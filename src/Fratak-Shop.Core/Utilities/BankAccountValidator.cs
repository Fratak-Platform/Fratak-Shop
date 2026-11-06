using System.Text.RegularExpressions;

namespace Fratak_Shop.Core.Utilities;

/// <summary>Normalization and validation of payout destinations (bank card, account number, holder name).</summary>
public static partial class BankAccountValidator
{
    /// <summary>Digits only (Persian / Arabic digits converted, spaces and dashes removed).</summary>
    public static string DigitsOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value
            .Select(ch => ch switch
            {
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                _ => ch,
            })
            .Where(char.IsAsciiDigit)
            .ToArray());
    }

    /// <summary>16 digit card number with a valid Luhn checksum.</summary>
    public static bool IsValidCardNumber(string? digits)
    {
        if (string.IsNullOrEmpty(digits) || digits.Length != 16 || !digits.All(char.IsAsciiDigit))
            return false;

        // All-same digits (0000…, 1111…) pass Luhn in some cases but are never real cards.
        if (digits.Distinct().Count() == 1)
            return false;

        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var digit = digits[digits.Length - 1 - i] - '0';
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
        }

        return sum % 10 == 0;
    }

    /// <summary>Account numbers: digits with optional separators (- . /), 6 to 26 digits.</summary>
    public static string? NormalizeAccountNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var converted = new string(value.Trim()
            .Select(ch => ch switch
            {
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                _ => ch,
            })
            .Where(ch => char.IsAsciiDigit(ch) || ch is '-' or '.' or '/')
            .ToArray())
            .Trim('-', '.', '/');

        return converted;
    }

    public static bool IsValidAccountNumber(string? normalized)
    {
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 30)
            return false;

        var digits = normalized.Count(char.IsAsciiDigit);
        return digits is >= 6 and <= 26;
    }

    /// <summary>Full name: letters (Persian / Latin), spaces, ZWNJ and dots, 3–200 characters.</summary>
    public static bool IsValidHolderName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 3 and <= 200 && HolderPattern().IsMatch(name.Trim())
        && name.Trim().Contains(' ');

    public static string MaskCard(string? digits) =>
        string.IsNullOrEmpty(digits) || digits.Length != 16
            ? digits ?? string.Empty
            : $"{digits[..4]}-{digits[4..6]}••-••••-{digits[12..]}";

    public static string FormatCard(string? digits) =>
        string.IsNullOrEmpty(digits) || digits.Length != 16
            ? digits ?? string.Empty
            : $"{digits[..4]}-{digits[4..8]}-{digits[8..12]}-{digits[12..]}";

    [GeneratedRegex(@"^[\p{L}\p{M}\s\u200C\.]+$")]
    private static partial Regex HolderPattern();
}
