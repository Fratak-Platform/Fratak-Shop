using Fratak_Shop.Core.Services.Seo;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// SEO-aware request pipeline:
/// 1) every 404 is logged (aggregated per path) so the SEO centre can show broken links,
/// 2) old URLs that have a redirect are served as a 301/302 instead of leaking link equity.
/// Redirects are cached for a few minutes to keep the database out of the hot path.
/// </summary>
public class SeoRoutingMiddleware(RequestDelegate next)
{
    private const string CacheKey = "fratak:seo:redirects";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(3);

    public async Task InvokeAsync(HttpContext context, ISeoService seoService, IMemoryCache cache)
    {
        if (ShouldSkip(context))
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "/";
        var redirect = await ResolveAsync(path, seoService, cache);

        if (redirect is not null && !string.Equals(redirect.FromPath, redirect.ToUrl, StringComparison.OrdinalIgnoreCase))
        {
            await seoService.RegisterRedirectHitAsync(redirect.Id);

            context.Response.Redirect(redirect.ToUrl, permanent: redirect.Type == Data.Entities.SeoRedirects.RedirectType.MovedPermanently ||
                                                            redirect.Type == Data.Entities.SeoRedirects.RedirectType.PermanentRedirect);
            return;
        }

        await next(context);

        // The status code is read after the pipeline ran; nothing is rewritten here,
        // so the response body is never buffered in memory.
        if (context.Response.StatusCode == StatusCodes.Status404NotFound)
        {
            await seoService.LogNotFoundAsync(path, context.Request.Headers.Referer.ToString(),
                context.Request.Headers.UserAgent.ToString());
        }
    }

    private static bool ShouldSkip(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            return true;

        var path = context.Request.Path.Value ?? "/";

        if (path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/uploads", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/assets", StringComparison.OrdinalIgnoreCase))
            return true;

        var lastSegment = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return lastSegment is not null && lastSegment.Contains('.');
    }

    /// <summary>ریدایرکت‌های فعال به‌صورت دیکشنری کش می‌شوند تا هر درخواست کوئری نزند.</summary>
    private static async Task<Data.Entities.SeoRedirects.SeoRedirect?> ResolveAsync(string path, ISeoService seoService,
        IMemoryCache cache)
    {
        var normalized = Normalize(path);

        if (!cache.TryGetValue(CacheKey, out List<Data.Entities.SeoRedirects.SeoRedirect>? redirects))
        {
            redirects = await seoService.GetRedirectsAsync();
            redirects = redirects.Where(redirect => redirect.IsActive).ToList();
            cache.Set(CacheKey, redirects, CacheDuration);
        }

        return redirects?.FirstOrDefault(redirect => redirect.IsActive &&
                                                     string.Equals(redirect.FromPath, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string path)
    {
        var normalized = path.Trim().ToLowerInvariant();
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;

        var queryIndex = normalized.IndexOf('?');
        if (queryIndex > 0)
            normalized = normalized[..queryIndex];

        var length = normalized.Length > 1 ? normalized.TrimEnd('/').Length : 1;
        return normalized[..length];
    }
}
