using Fratak_Shop.Core.DTOs.Shopping.Command;
using Fratak_Shop.Core.DTOs.Shopping.Query;
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
public class CartModel(ICartService cartService, ICartSessionService cartSession, IPageMetaService pageMeta) : ShopPageModel
{
    public CartDto? Cart { get; private set; }

    public bool IsEmpty => Cart is null || Cart.Items.Count == 0;

    /// <summary>Rows whose product was removed or no longer has enough stock.</summary>
    public bool HasProblems => Cart?.Items.Any(i => !i.HasEnoughStock) == true;

    public async Task OnGetAsync()
    {
        Cart = await cartSession.GetCartAsync();
        await pageMeta.ApplyAsync(ViewData, SitePage.Cart, "سبد خرید");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
    }

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
