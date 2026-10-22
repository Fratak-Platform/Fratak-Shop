using Fratak_Shop.Core.Services.SeoRedirects;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure.Middleware;

/// <summary>
/// Applies the redirects managed by the SEO specialist (old product slugs,
/// removed pages, ...). Lookups are cached so ordinary requests do not hit the
/// database every time.
/// </summary>
public class SeoRedirectMiddleware(RequestDelegate next, IMemoryCache cache, ILogger<SeoRedirectMiddleware> logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly string[] IgnoredPrefixes = ["/assets", "/uploads", "/lib", "/css", "/js", "/_"];

    public async Task InvokeAsync(HttpContext context, ISeoRedirectService redirectService)
    {
        var path = context.Request.Path.Value;

        if (!HttpMethods.IsGet(context.Request.Method)
            || string.IsNullOrEmpty(path)
            || path == "/"
            || Path.HasExtension(path)
            || IgnoredPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var normalizedPath = path.TrimEnd('/').ToLowerInvariant();

        var redirect = await cache.GetOrCreateAsync($"seo-redirect:{normalizedPath}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            try
            {
                var result = await redirectService.GetRedirectByPathAsync(normalizedPath);
                return result is null ? null : new CachedRedirect(result.ToUrl, (int)result.Type);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "SEO redirect lookup failed for {Path}", normalizedPath);
                return null;
            }
        });

        if (redirect is not null && !string.Equals(redirect.ToUrl.TrimEnd('/'), normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = redirect.StatusCode;
            context.Response.Headers.Location = redirect.ToUrl + context.Request.QueryString;
            return;
        }

        await next(context);
    }

    private sealed record CachedRedirect(string ToUrl, int StatusCode);
}
