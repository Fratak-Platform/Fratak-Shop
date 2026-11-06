namespace Fratak_Shop.Web.Services;

/// <summary>
/// Product comparison list kept in a cookie (no database round trip, works for guests).
/// </summary>
public interface ICompareListService
{
    const int MaxItems = 4;

    IReadOnlyList<Guid> GetProductIds();

    /// <returns>false when the list is already full.</returns>
    bool Add(Guid productId);

    void Remove(Guid productId);

    void Clear();
}

public class CompareListService(IHttpContextAccessor httpContextAccessor) : ICompareListService
{
    private const string CookieName = ".Fratak.Compare";

    public IReadOnlyList<Guid> GetProductIds()
    {
        var raw = httpContextAccessor.HttpContext?.Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => Guid.TryParse(part, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(ICompareListService.MaxItems)
            .ToList();
    }

    public bool Add(Guid productId)
    {
        var ids = GetProductIds().ToList();

        if (ids.Contains(productId))
            return true;

        if (ids.Count >= ICompareListService.MaxItems)
            return false;

        ids.Add(productId);
        Write(ids);
        return true;
    }

    public void Remove(Guid productId)
    {
        var ids = GetProductIds().Where(id => id != productId).ToList();
        Write(ids);
    }

    public void Clear() => Write([]);

    private void Write(List<Guid> ids)
    {
        var context = httpContextAccessor.HttpContext;
        if (context is null)
            return;

        if (ids.Count == 0)
        {
            context.Response.Cookies.Delete(CookieName);
            return;
        }

        context.Response.Cookies.Append(CookieName, string.Join('.', ids.Select(id => id.ToString("N"))), new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
        });
    }
}
