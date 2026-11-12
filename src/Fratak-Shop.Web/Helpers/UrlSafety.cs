namespace Fratak_Shop.Web.Helpers;

/// <summary>
/// Guards links that come from the database (admin panel): only http(s) URLs and
/// site-relative paths are rendered, so a stored <c>javascript:</c> URL can't become XSS.
/// </summary>
public static class UrlSafety
{
    public static bool IsHttpUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>"/path" (not "//host") or an absolute http(s) URL.</summary>
    public static bool IsSafeLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var value = url.Trim();
        if (value.StartsWith('/') && !value.StartsWith("//") && !value.StartsWith("/\\"))
            return true;

        return IsHttpUrl(value);
    }

    /// <summary>The link when safe, otherwise <paramref name="fallback"/>.</summary>
    public static string Link(string? url, string fallback = "#") => IsSafeLink(url) ? url!.Trim() : fallback;

    /// <summary>True for links that leave the site (rendered with rel="noopener").</summary>
    public static bool IsExternal(string? url) => IsHttpUrl(url);

    /// <summary>Local return URL check (open redirect protection).</summary>
    public static bool IsLocal(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\");
}
