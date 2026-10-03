using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shipping;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

public class CartModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IShippingMethodService shipping)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public CartDto Cart { get; private set; } = new();

    public List<ShippingMethodDto> ShippingMethods { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public Guid? shippingMethodId { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Cart = await cart.GetCartDtoAsync(CurrentUserId, CartSessionKey, shippingMethodId: shippingMethodId);
        ShippingMethods = await shipping.GetActiveAsync();

        ApplySeo(null, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            Path = "/cart",
            ContentTitle = "سبد خرید",
            ShortDescription = "مرور کالاهای انتخابی، اعمال کد تخفیف و ادامه فرآیند خرید.",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = true,
        });

        return Page();
    }

    /// <summary>محتوای کشویی سبد خرید در هدر (درخواست AJAX).</summary>
    public async Task<IActionResult> OnGetMiniAsync()
    {
        var model = await cart.GetCartDtoAsync(CurrentUserId, CartSessionKey);
        return Partial("_MiniCart", model);
    }

    public async Task<IActionResult> OnPostAddAsync(Guid productId, int quantity = 1)
    {
        var result = await cart.AddToCartAsync(productId, quantity, CurrentUserId, CartSessionKey);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var count = await cart.GetItemCountAsync(CurrentUserId, CartSessionKey);
            return new JsonResult(new
            {
                success = result.IsSuccess(),
                message = result.Message,
                count,
            });
        }

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Cart");
    }

    public async Task<IActionResult> OnPostUpdateAsync(Guid productId, int quantity)
    {
        var result = await cart.SetQuantityAsync(productId, quantity, CurrentUserId, CartSessionKey);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Cart");
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid productId)
    {
        var result = await cart.RemoveAsync(productId, CurrentUserId, CartSessionKey);

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var count = await cart.GetItemCountAsync(CurrentUserId, CartSessionKey);
            var model = await cart.GetCartDtoAsync(CurrentUserId, CartSessionKey);
            return new JsonResult(new { success = result.IsSuccess(), message = result.Message, count, total = model.PayableAmount.ToToman() });
        }

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Cart");
    }

    public async Task<IActionResult> OnPostApplyCouponAsync(string code)
    {
        var result = await cart.ApplyCouponAsync(code ?? string.Empty, CurrentUserId, CartSessionKey);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Cart");
    }

    public async Task<IActionResult> OnPostRemoveCouponAsync()
    {
        var result = await cart.RemoveCouponAsync(CurrentUserId, CartSessionKey);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Cart");
    }

    public async Task<IActionResult> OnPostClearAsync()
    {
        var result = await cart.ClearAsync(CurrentUserId, CartSessionKey);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Cart");
    }

    public class CartInput
    {
        public string? Code { get; set; }
    }
}
