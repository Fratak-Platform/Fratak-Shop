using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

public class CheckoutFailedModel(IOrderService orderService) : ShopPageModel
{
    public OrderDto Order { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string orderNumber)
    {
        var order = await orderService.GetOrderByNumberAsync(orderNumber);
        if (order is null || order.UserId != CurrentUserId)
            return NotFound();

        Order = order;
        ViewData[PageMetaKeys.Title] = "پرداخت ناموفق";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }
}
