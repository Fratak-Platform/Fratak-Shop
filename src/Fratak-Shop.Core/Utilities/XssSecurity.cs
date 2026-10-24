using System.Net;
using System.Text.RegularExpressions;
using Ganss.Xss;

namespace Fratak_Shop.Core.Utilities;

public static partial class XssSecurity
{
    // Sanitizing is read-only after construction, so a single configured instance is
    // shared (building an HtmlSanitizer per call is expensive).
    private static readonly HtmlSanitizer Sanitizer = new()
    {
        KeepChildNodes = true,
        AllowDataAttributes = true,
    };

    /// <summary>Removes dangerous markup (scripts, event handlers, javascript: urls) and keeps safe HTML.</summary>
    public static string SanitizeText(this string text)
    {
        return string.IsNullOrEmpty(text) ? string.Empty : Sanitizer.Sanitize(text);
    }

    /// <summary><see cref="SanitizeText"/> for optional values: empty input becomes null.</summary>
    public static string? SanitizeOrNull(this string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : Sanitizer.Sanitize(text.Trim());
    }

    /// <summary>Plain text version of HTML (for meta descriptions, JSON-LD, search snippets).</summary>
    public static string ToPlainText(this string? html, int maxLength = 0)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = WebUtility.HtmlDecode(TagRegex().Replace(html, " "));
        text = WhitespaceRegex().Replace(text, " ").Trim();

        if (maxLength > 0 && text.Length > maxLength)
        {
            var cut = text.LastIndexOf(' ', maxLength - 1);
            text = (cut > maxLength / 2 ? text[..cut] : text[..(maxLength - 1)]).TrimEnd('،', ',', '.', ' ') + "…";
        }

        return text;
    }

    [GeneratedRegex("<[^>]*>", RegexOptions.Compiled, 250)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled, 250)]
    private static partial Regex WhitespaceRegex();
}
