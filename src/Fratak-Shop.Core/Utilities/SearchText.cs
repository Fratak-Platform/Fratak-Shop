namespace Fratak_Shop.Core.Utilities;

/// <summary>Normalization of Persian search input (Arabic ي/ك, digits, extra spaces).</summary>
public static class SearchText
{
    public const int MaxLength = 100;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value.Trim().Select(ch => ch switch
        {
            'ي' or 'ى' => 'ی',
            'ك' => 'ک',
            'ة' => 'ه',
            'ۀ' => 'ه',
            >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
            >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
            '\u200C' => ' ',
            _ when char.IsControl(ch) => ' ',
            _ => ch,
        });

        var text = string.Join(' ', new string(chars.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return text.Length > MaxLength ? text[..MaxLength] : text;
    }

    /// <summary>Persian / Arabic digits → ASCII digits, trimmed (admin search boxes: order numbers, phones).</summary>
    public static string NormalizeDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return new string(value.Trim().Select(ch => ch switch
        {
            >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
            >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
            'ي' => 'ی',
            'ك' => 'ک',
            _ => ch,
        }).ToArray());
    }

    /// <summary>Separate words (at most 5) – every word must match.</summary>
    public static List<string> Terms(string? value) =>
        Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
}
