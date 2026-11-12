using System.Text.RegularExpressions;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// Validation and safe rendering data of product videos. Only three shapes are accepted, so a
/// stored value can never become an arbitrary iframe / script URL:
/// uploaded file name, Aparat video hash (or its URL) and a direct https mp4/webm link.
/// </summary>
public static partial class VideoSource
{
    public sealed record Resolved(ProductVideoProvider Provider, string? EmbedUrl, string? FileUrl, string? MimeType, string PageUrl);

    /// <summary>Normalizes admin input; null when not acceptable.</summary>
    public static string? Normalize(ProductVideoProvider provider, string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 1000)
            return null;

        return provider switch
        {
            ProductVideoProvider.Aparat => AparatHash(value),
            ProductVideoProvider.DirectUrl => IsDirectFile(value) ? value : null,
            ProductVideoProvider.Upload => UploadName().IsMatch(value) ? value : null,
            _ => null,
        };
    }

    public static Resolved? Resolve(ProductVideoProvider provider, string source)
    {
        var normalized = Normalize(provider, source);
        if (normalized is null)
            return null;

        return provider switch
        {
            ProductVideoProvider.Aparat => new Resolved(provider,
                $"https://www.aparat.com/video/video/embed/videohash/{normalized}/vt/frame", null, null,
                $"https://www.aparat.com/v/{normalized}"),
            ProductVideoProvider.DirectUrl => new Resolved(provider, null, normalized, MimeOf(normalized), normalized),
            ProductVideoProvider.Upload => new Resolved(provider, null, "/uploads/videos/" + normalized, MimeOf(normalized), "/uploads/videos/" + normalized),
            _ => null,
        };
    }

    private static string? AparatHash(string value)
    {
        if (AparatId().IsMatch(value))
            return value;

        var match = AparatUrl().Match(value);
        return match.Success ? match.Groups["hash"].Value : null;
    }

    private static bool IsDirectFile(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase));

    private static string MimeOf(string value) =>
        value.Split('?')[0].EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ? "video/webm" : "video/mp4";

    [GeneratedRegex("^[A-Za-z0-9]{3,24}$")]
    private static partial Regex AparatId();

    [GeneratedRegex(@"^https://(www\.)?aparat\.com/(v/|video/video/embed/videohash/)(?<hash>[A-Za-z0-9]{3,24})(/.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex AparatUrl();

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,100}\.(mp4|webm)$", RegexOptions.IgnoreCase)]
    private static partial Regex UploadName();
}
