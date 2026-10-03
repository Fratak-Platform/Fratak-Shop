using Fratak_Shop.Core.Services.Favorites;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

[Authorize(Policy = PolicyNames.CustomerOnly)]
public class FavoritesModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IFavoriteService favorites,
    IProductService products)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public List<ProductCardDto> Items { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login?returnUrl=/profile/favorites");

        var favoriteItems = await favorites.GetFavoritesByUserIdAsync(userId);
        Items = await products.GetCardsByIdsAsync(favoriteItems.Select(item => item.ProductId));

        ApplySeo(null, BuildFallback());
        return Page();
    }

    /// <summary>افزودن/حذف علاقه‌مندی از طریق آیکن قلب در صفحات فروشگاه.</summary>
    public async Task<IActionResult> OnPostToggleAsync(Guid productId)
    {
        if (CurrentUserId is not { } userId)
            return new JsonResult(new { authenticated = false, message = "برای افزودن به علاقه‌مندی‌ها وارد شوید." });

        var existing = (await favorites.GetFavoritesByUserIdAsync(userId)).FirstOrDefault(item => item.ProductId == productId);

        var result = existing is null
            ? await favorites.AddFavoriteAsync(new Core.DTOs.Favorites.Command.CreateFavoriteCommand { UserId = userId, ProductId = productId })
            : await favorites.RemoveFavoriteAsync(userId, productId);

        return new JsonResult(new
        {
            authenticated = true,
            isFavorite = existing is null,
            message = result.Message,
        });
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid productId)
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login");

        var result = await favorites.RemoveFavoriteAsync(userId, productId);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Profile/Favorites");
    }

    private SeoFallback BuildFallback() => new()
    {
        SiteName = Site.SiteName,
        SiteUrl = Site.SiteUrl,
        Path = "/profile/favorites",
        ContentTitle = "علاقه‌مندی‌های من",
        EntityType = SeoEntityType.Page,
        ForceNoIndex = true,
    };
}
