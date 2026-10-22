using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Pages.Payment;

/// <summary>
/// Fake bank page of the sandbox gateway (development / demo only).
/// </summary>
public class SandboxModel(IOrderService orderService, IOptions<ShopOptions> shopOptions) : ShopPageModel
{
    public OrderDto Order { get; private set; } = null!;
    public string CallbackUrl { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(string orderNumber, string? callback)
    {
        if (!shopOptions.Value.EnableSandboxGateway)
            return NotFound();

        var order = await orderService.GetOrderByNumberAsync(orderNumber);
        if (order is null || order.UserId != CurrentUserId)
            return NotFound();

        // Only callbacks to this site are accepted (no open redirect).
        var expected = Url.Page("/Payment/Callback", null, new { orderNumber }, Request.Scheme)!;
        CallbackUrl = string.Equals(callback, expected, StringComparison.OrdinalIgnoreCase)
            ? callback!
            : Url.Page("/Payment/Callback", new { orderNumber })!;

        Order = order;
        ViewData[PageMetaKeys.Title] = "درگاه پرداخت آزمایشی";
        return Page();
    }
}
