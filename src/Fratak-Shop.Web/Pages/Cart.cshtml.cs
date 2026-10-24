using Fratak_Shop.Core.DTOs.Marketing;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.DTOs.Shopping.Command;
using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Shopping cart page plus the cart actions used across the site
/// (add from product cards / product page, change quantity, remove).
/// </summary>
public class CartModel(
    ICartService cartService,
    ICartSessionService cartSession,
    ICouponService couponService,
    IRecommendationService recommendations,
    ISiteContext site,
    IPageMetaService pageMeta) : ShopPageModel
{
    public CartDto? Cart { get; private set; }

    public bool IsEmpty => Cart is null || Cart.Items.Count == 0;

    /// <summary>Rows whose product was removed or no longer has enough stock.</summary>
    public bool HasProblems => Cart?.Items.Any(i => !i.HasEnoughStock) == true;

    public CouponEvaluation? Coupon { get; private set; }

    public string? CouponError { get; private set; }

    public List<ProductFilterData> Suggestions { get; private set; } = [];

    public decimal PayableAfterCoupon => Math.Max(0, (Cart?.PayableAmount ?? 0) - (Coupon?.ItemsDiscount ?? 0));

    public async Task OnGetAsync()
    {
        Cart = await cartSession.GetCartAsync();
        await pageMeta.ApplyAsync(ViewData, SitePage.Cart, "سبد خرید");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";

        if (Cart is { Items.Count: > 0 } && site.Settings.IsCouponEnabled && !string.IsNullOrWhiteSpace(Cart.CouponCode))
        {
            var evaluation = await EvaluateAsync(Cart, Cart.CouponCode);
            if (Succeeded(evaluation))
                Coupon = evaluation.Data;
            else
                CouponError = evaluation.Message;
        }

        if (site.Settings.IsRecommendationEnabled)
        {
            var inCart = Cart?.Items.Select(i => i.ProductId).ToList() ?? [];
            Suggestions = inCart.Count > 0
                ? await recommendations.GetForCartAsync(inCart, CurrentUserId, 10)
                : await recommendations.GetPersonalizedAsync(CurrentUserId, RecentlyViewed.Read(Request), [], 10);
        }
    }

    /// <summary>Checks a discount code against the cart and stores it (used by the cart and the checkout).</summary>
    public async Task<IActionResult> OnPostCouponAsync(string? code, string? returnUrl = null)
    {
        var cart = await cartSession.GetCartAsync();
        if (cart is null || cart.Items.Count == 0 || string.IsNullOrWhiteSpace(code))
            return RedirectToLocal(returnUrl, "/Cart");

        if (!site.Settings.IsCouponEnabled)
        {
            ErrorMessage = "استفاده از کد تخفیف در حال حاضر غیرفعال است.";
            return RedirectToLocal(returnUrl, "/Cart");
        }

        var evaluation = await EvaluateAsync(cart, code);
        if (!Succeeded(evaluation) || evaluation.Data is null)
        {
            ErrorMessage = evaluation.Message;
            return RedirectToLocal(returnUrl, "/Cart");
        }

        await cartService.SetCouponAsync(cart.Id, evaluation.Data.Code);
        SuccessMessage = evaluation.Message;
        return RedirectToLocal(returnUrl, "/Cart");
    }

    public async Task<IActionResult> OnPostRemoveCouponAsync(string? returnUrl = null)
    {
        var cart = await cartSession.GetCartAsync();
        if (cart is not null)
            ShowResult(await cartService.SetCouponAsync(cart.Id, null));

        return RedirectToLocal(returnUrl, "/Cart");
    }

    private Task<Fratak_Shop.Core.Utilities.OperationResult<CouponEvaluation>> EvaluateAsync(CartDto cart, string code) =>
        couponService.EvaluateAsync(new CouponEvaluationRequest
        {
            Code = code,
            UserId = CurrentUserId,
            Lines = cart.Items.Select(i => new CouponLine(i.ProductId, i.Quantity)).ToList(),
            // Guests may preview a code; per-customer rules are checked again at checkout.
            RequireUser = false,
        });

    public async Task<IActionResult> OnPostAddAsync(Guid productId, int quantity = 1, string? returnUrl = null)
    {
        var cart = await cartSession.GetOrCreateCartAsync();
        if (cart is null)
        {
            ErrorMessage = "امکان ایجاد سبد خرید وجود ندارد.";
            return RedirectToLocal(returnUrl, "/Cart");
        }

        var result = await cartService.AddItemAsync(new AddCartItemCommand
        {
            CartId = cart.Id,
            ProductId = productId,
            Quantity = Math.Max(1, quantity),
        });

        ShowResult(result);
        return RedirectToLocal(returnUrl, "/Cart");
    }

    public async Task<IActionResult> OnPostUpdateAsync(Guid productId, int quantity, string? returnUrl = null)
    {
        var cart = await cartSession.GetCartAsync();
        if (cart is null)
            return RedirectToLocal(returnUrl, "/Cart");

        var result = quantity <= 0
            ? await cartService.RemoveItemAsync(new RemoveCartItemCommand { CartId = cart.Id, ProductId = productId })
            : await cartService.UpdateItemAsync(new UpdateCartItemCommand { CartId = cart.Id, ProductId = productId, Quantity = quantity });

        if (!Succeeded(result))
            ErrorMessage = result.Message;

        return RedirectToLocal(returnUrl, "/Cart");
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid productId, string? returnUrl = null)
    {
        var cart = await cartSession.GetCartAsync();
        if (cart is not null)
        {
            var result = await cartService.RemoveItemAsync(new RemoveCartItemCommand { CartId = cart.Id, ProductId = productId });
            ShowResult(result);
        }

        return RedirectToLocal(returnUrl, "/Cart");
    }

    public async Task<IActionResult> OnPostClearAsync()
    {
        var cart = await cartSession.GetCartAsync();
        if (cart is not null)
            ShowResult(await cartService.ClearCartAsync(cart.Id));

        return RedirectToPage();
    }
}
