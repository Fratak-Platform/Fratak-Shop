using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Web.Infrastructure;

namespace Fratak_Shop.Web.Services;

/// <summary>
/// Resolves the shopping cart of the current visitor: the customer's cart when
/// signed in, otherwise a guest cart identified by a long-lived cookie.
/// </summary>
public interface ICartSessionService
{
    /// <summary>Current cart without creating one (null when the visitor has no cart yet).</summary>
    Task<CartDto?> GetCartAsync();

    /// <summary>Current cart, created on demand.</summary>
    Task<CartDto?> GetOrCreateCartAsync();

    /// <summary>Moves the guest cart (if any) into the customer's cart after sign-in.</summary>
    Task MergeGuestCartAsync(Guid userId);
}

public class CartSessionService(IHttpContextAccessor httpContextAccessor, ICartService cartService) : ICartSessionService
{
    public const string GuestCookieName = ".Fratak.Cart";

    private CartDto? _cachedCart;
    private bool _loaded;

    public async Task<CartDto?> GetCartAsync()
    {
        if (_loaded)
            return _cachedCart;

        var userId = httpContextAccessor.HttpContext?.User.GetUserId();

        if (userId.HasValue)
        {
            _cachedCart = await cartService.GetCartByUserAsync(userId.Value);
        }
        else
        {
            var guestKey = ReadGuestKey();
            _cachedCart = guestKey is null ? null : await cartService.GetCartBySessionAsync(guestKey);
        }

        _loaded = true;
        return _cachedCart;
    }

    public async Task<CartDto?> GetOrCreateCartAsync()
    {
        var userId = httpContextAccessor.HttpContext?.User.GetUserId();
        var guestKey = userId.HasValue ? null : ReadGuestKey() ?? CreateGuestKey();

        // نتیجه کش‌شده درخواست جاری باطل می‌شود چون ممکن است سبد تازه ساخته شود.
        _loaded = false;
        return await cartService.GetOrCreateCartAsync(userId, guestKey);
    }

    public async Task MergeGuestCartAsync(Guid userId)
    {
        var guestKey = ReadGuestKey();
        if (guestKey is null)
            return;

        var guestCart = await cartService.GetCartBySessionAsync(guestKey);
        if (guestCart is not null && guestCart.Items.Count > 0)
            await cartService.MergeGuestCartAsync(guestKey, userId);

        httpContextAccessor.HttpContext?.Response.Cookies.Delete(GuestCookieName);
        _loaded = false;
    }

    private string? ReadGuestKey()
    {
        var value = httpContextAccessor.HttpContext?.Request.Cookies[GuestCookieName];
        return Guid.TryParse(value, out var key) ? key.ToString("N") : null;
    }

    private string CreateGuestKey()
    {
        var key = Guid.NewGuid().ToString("N");

        httpContextAccessor.HttpContext?.Response.Cookies.Append(GuestCookieName, key, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = httpContextAccessor.HttpContext.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
        });

        return key;
    }
}
