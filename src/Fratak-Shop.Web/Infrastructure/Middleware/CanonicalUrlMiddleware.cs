namespace Fratak_Shop.Web.Infrastructure.Middleware;

/// <summary>
/// One URL per page (duplicate-content protection): page URLs are served lowercase and
/// without a trailing slash, and the old static-page addresses permanently redirect to
/// the database-driven content pages. Only GET/HEAD requests for pages are touched;
/// static files, APIs and query strings are left as they are.
/// </summary>
public class CanonicalUrlMiddleware(RequestDelegate next)
{
    private static readonly string[] IgnoredPrefixes = ["/assets", "/uploads", "/lib", "/api", "/_", "/.well-known", "/admin", "/health", "/hubs",
        // توکن لینک پرداخت به حروف کوچک/بزرگ حساس است؛ کوچک کردن مسیر، لینک را خراب می‌کند.
        "/pay/"];

    /// <summary>Old hard-coded pages → content page slugs.</summary>
    private static readonly Dictionary<string, string> LegacyPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/aboutus"] = "/page/about-us",
        ["/about-us"] = "/page/about-us",
        ["/about"] = "/page/about-us",
        ["/rules"] = "/page/rules",
        ["/terms"] = "/page/rules",
        ["/privacy"] = "/page/privacy",
        ["/privacy-policy"] = "/page/privacy",
        ["/return-policy"] = "/page/return-policy",
        ["/returns"] = "/page/return-policy",
        ["/warranty"] = "/page/warranty",
        ["/shipping"] = "/page/shipping",
        ["/payment-guide"] = "/page/payment-guide",
        ["/contact"] = "/contact-us",
        ["/contactus"] = "/contact-us",
    };

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var path = request.Path.Value;

        // Re-executed error pages (404, 500) must keep their status code.
        if (context.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodeReExecuteFeature>() is not null
            || context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>() is not null)
            return next(context);

        if (!(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            || string.IsNullOrEmpty(path)
            || path == "/"
            || Path.HasExtension(path)
            || IgnoredPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return next(context);
        }

        var target = path.Length > 1 ? path.TrimEnd('/') : path;
        if (target.Length == 0)
            target = "/";

        if (LegacyPaths.TryGetValue(target, out var legacy))
            target = legacy;
        else if (HasUpperAscii(target))
            target = LowerAscii(target);

        if (string.Equals(target, path, StringComparison.Ordinal))
            return next(context);

        // Path segments are already decoded; re-encode through PathString.
        context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
        context.Response.Headers.Location = $"{request.PathBase}{new PathString(target).ToUriComponent()}{request.QueryString}";
        return Task.CompletedTask;
    }

    // Only ASCII letters are lowered: Persian slugs have no case and must stay untouched.
    private static bool HasUpperAscii(string value) => value.Any(c => c is >= 'A' and <= 'Z');

    private static string LowerAscii(string value) =>
        string.Create(value.Length, value, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i];
        });
}
