using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services.Payments;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Payment;

/// <summary>
/// Sends the customer to ZarinPal for an unpaid order. POST only (antiforgery
/// protected) because it registers a new gateway transaction.
/// </summary>
public class StartModel(IOrderService orderService, IPaymentFlowService paymentFlow) : ShopPageModel
{
    public IActionResult OnGet(string orderNumber) => RedirectToPage("/Profile/OrderDetail", new { orderNumber });

    public async Task<IActionResult> OnPostAsync(string orderNumber)
    {
        var order = await orderService.GetOrderByNumberAsync(orderNumber);
        if (order is null || order.UserId != CurrentUserId)
            return NotFound();

        if (order.PaymentStatus == PaymentStatus.Paid)
            return RedirectToPage("/CheckoutComplete", new { orderNumber });

        var start = await paymentFlow.StartOrderPaymentAsync(order, CurrentUserId!.Value);
        if (!start.Succeeded)
        {
            ErrorMessage = start.Error;
            return RedirectToPage("/Profile/OrderDetail", new { orderNumber });
        }

        return Redirect(start.RedirectUrl!);
    }
}
