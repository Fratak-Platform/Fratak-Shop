using System.Text;

namespace Fratak_Shop.Core.Utilities;

public static class SlugHelper
{
    /// <summary>
    /// URL friendly slug that keeps Persian letters: "درباره ما!" → "درباره-ما",
    /// "About Us" → "about-us". Only letters, digits and single dashes remain.
    /// </summary>
    public static string ToSlug(this string? value, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;

        foreach (var raw in value.Trim().ToLowerInvariant())
        {
            var ch = raw switch
            {
                'ي' => 'ی',
                'ك' => 'ک',
                >= '۰' and <= '۹' => (char)('0' + (raw - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (raw - '٠')),
                _ => raw,
            };

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }

            if (builder.Length >= maxLength)
                break;
        }

        return builder.ToString().Trim('-');
    }
}
