namespace Fratak_Shop.Web.Services;

/// <summary>
/// Recently viewed products of the browser (signal for personal suggestions). Only
/// product ids are stored, in an HttpOnly cookie; nothing is kept on the server.
/// </summary>
public static class RecentlyViewed
{
    private const string CookieName = "fratak_rv";
    private const int MaxItems = 12;

    public static List<Guid> Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrWhiteSpace(value) || value.Length > 600)
            return [];

        return value.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Guid.TryParseExact(part, "N", out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(MaxItems)
            .ToList();
    }

    public static void Push(HttpContext context, Guid productId)
    {
        var ids = Read(context.Request);
        ids.Remove(productId);
        ids.Insert(0, productId);

        context.Response.Cookies.Append(CookieName, string.Join('.', ids.Take(MaxItems).Select(id => id.ToString("N"))), new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(30),
            IsEssential = true,
        });
    }
}
