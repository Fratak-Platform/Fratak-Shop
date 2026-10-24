using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Infrastructure.PageCache;

/// <summary>
/// The cookie TempData provider sends a "delete cookie" header on every page that touches TempData,
/// even when the visitor never had the cookie. That header made every public page uncacheable
/// (responses with Set-Cookie are never stored). This wrapper skips the pointless delete.
/// </summary>
public sealed class CacheFriendlyTempDataProvider(CookieTempDataProvider inner, IOptions<CookieTempDataProviderOptions> options) : ITempDataProvider
{
    private readonly string _cookieName = options.Value.Cookie.Name ?? ".AspNetCore.Mvc.CookieTempDataProvider";

    public IDictionary<string, object> LoadTempData(HttpContext context) => inner.LoadTempData(context);

    public void SaveTempData(HttpContext context, IDictionary<string, object> values)
    {
        if ((values is null || values.Count == 0) && !HasTempDataCookie(context.Request))
            return; // nothing to store and nothing to delete

        inner.SaveTempData(context, values);
    }

    private bool HasTempDataCookie(HttpRequest request)
    {
        foreach (var name in request.Cookies.Keys)
        {
            if (name.StartsWith(_cookieName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
