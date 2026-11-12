using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.SeoRedirects;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Fratak_Shop.Data.Utilities;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure.Middleware;

/// <summary>
/// Applies the redirects managed by the SEO specialist (old product slugs, removed pages, ...).
/// Lookups are cached per path; the cache key carries the redirect table's change version so an
/// edited / added redirect works on the very next request. Used redirects and pages that end in
/// 404 are counted in memory (<see cref="SeoTrafficBuffer"/>) and written in batches.
/// </summary>
public class SeoRedirectMiddleware(RequestDelegate next, IMemoryCache cache, ILogger<SeoRedirectMiddleware> logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly string[] IgnoredPrefixes = ["/assets", "/uploads", "/lib", "/css", "/js", "/_", "/api", "/media", "/payment", "/.well-known", "/admin", "/health"];

    public async Task InvokeAsync(HttpContext context, ISeoRedirectService redirectService)
    {
        var path = context.Request.Path.Value;

        if (!(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            || string.IsNullOrEmpty(path)
            || path == "/"
            || Path.HasExtension(path)
            || IgnoredPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var normalizedPath = path.TrimEnd('/').ToLowerInvariant();
        var version = DataChangeNotifier.VersionOf(typeof(SeoRedirect));

        var redirect = await cache.GetOrCreateAsync($"seo-redirect:{version}:{normalizedPath}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            entry.Size = 1;

            try
            {
                var result = await redirectService.GetRedirectByPathAsync(normalizedPath);
                return result is null ? null : new CachedRedirect(result.Id, result.ToUrl, (int)result.Type);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "SEO redirect lookup failed for {Path}", normalizedPath);
                return null;
            }
        });

        if (redirect is not null && !string.Equals(redirect.ToUrl.TrimEnd('/'), normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            SeoTrafficBuffer.RecordRedirect(redirect.Id);
            context.Response.StatusCode = redirect.StatusCode;
            context.Response.Headers.Location = redirect.ToUrl + context.Request.QueryString;
            return;
        }

        await next(context);

        if (context.Response.StatusCode == StatusCodes.Status404NotFound
            && normalizedPath.Length <= 300
            && normalizedPath != "/404"
            && context.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodeReExecuteFeature>() is null)
        {
            var referrer = context.Request.Headers.Referer.ToString();
            SeoTrafficBuffer.RecordNotFound(normalizedPath, string.IsNullOrEmpty(referrer) ? null : referrer);
        }
    }

    private sealed record CachedRedirect(Guid Id, string ToUrl, int StatusCode);
}
