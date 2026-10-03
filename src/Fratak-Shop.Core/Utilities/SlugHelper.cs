using System.Globalization;
using System.Text;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// Turns Persian/English titles into URL safe slugs. Persian letters are kept as
/// they are (search engines and browsers handle them perfectly and a Persian slug
/// reads much better in the address bar), while every character that would break
/// a URL is normalised away.
/// </summary>
public static class SlugHelper
{
    private static readonly char[] Separators = [' ', '‌', '\u200c', '-', '_', '.', ',', '،', ':', '؛', ';', '/', '\\'];

    /// <summary>
    /// Normalises Persian text: Arabic ي/ك to Persian ی/ک, Persian/Arabic digits to
    /// ASCII digits and removal of the Arabic diacritics.
    /// </summary>
    public static string NormalizePersian(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var builder = new StringBuilder(input.Length);
        foreach (var character in input.Trim())
        {
            builder.Append(character switch
            {
                'ي' => 'ی',
                'ك' => 'ک',
                'ة' => 'ه',
                'ۀ' => 'ه',
                'أ' or 'إ' or 'آ' => 'ا',
                'ؤ' => 'و',
                '\u064B' or '\u064C' or '\u064D' or '\u064E' or '\u064F' or '\u0650' or '\u0651' or '\u0652' => '\0',
                _ => character,
            });
        }

        return builder.ToString().Replace("\0", string.Empty);
    }

    /// <summary>
    /// Replaces Persian digits with their Latin equivalent (used before parsing numbers).
    /// </summary>
    public static string ToLatinDigits(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var builder = new StringBuilder(input.Length);
        foreach (var character in input)
        {
            builder.Append(character switch
            {
                >= '۰' and <= '۹' => (char)(character - '۰' + '0'),
                >= '٠' and <= '٩' => (char)(character - '٠' + '0'),
                _ => character,
            });
        }

        return builder.ToString();
    }

    /// <summary>
    /// Converts a title to a slug: «هدفون بلوتوثی مدل X10» → «هدفون-بلوتوثی-مدل-x10».
    /// </summary>
    public static string Generate(string? title, int maxLength = 80)
    {
        var normalized = NormalizePersian(title);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized.ToLowerInvariant())
        {
            if (Separators.Contains(character))
            {
                builder.Append('-');
                continue;
            }

            if (char.IsLetterOrDigit(character) || character == '‌' || character == '-')
                builder.Append(character);
        }

        var slug = builder.ToString();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, "-{2,}", "-").Trim('-');

        if (slug.Length > maxLength)
            slug = slug[..maxLength].Trim('-');

        return slug;
    }
}
