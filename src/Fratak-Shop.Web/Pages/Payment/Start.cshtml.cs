using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services.Payments;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Payment;

/// <summary>Sends the customer to the payment gateway for an unpaid order.</summary>
public class StartModel(IOrderService orderService, IPaymentGateway gateway) : ShopPageModel
{
    public async Task<IActionResult> OnGetAsync(string orderNumber)
    {
        var order = await orderService.GetOrderByNumberAsync(orderNumber);
        if (order is null || order.UserId != CurrentUserId)
            return NotFound();

        if (order.PaymentStatus == PaymentStatus.Paid)
            return RedirectToPage("/CheckoutComplete", new { orderNumber });

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Returned)
        {
            ErrorMessage = "این سفارش لغو شده و قابل پرداخت نیست.";
            return RedirectToPage("/Profile/Orders");
        }

        var callbackUrl = Url.Page("/Payment/Callback", null, new { orderNumber }, Request.Scheme)!;
        var request = await gateway.RequestAsync(order, callbackUrl);

        if (!request.Succeeded || string.IsNullOrWhiteSpace(request.RedirectUrl))
        {
            ErrorMessage = request.Error ?? "اتصال به درگاه پرداخت برقرار نشد.";
            return RedirectToPage("/CheckoutFailed", new { orderNumber });
        }

        return Redirect(request.RedirectUrl);
    }
}
