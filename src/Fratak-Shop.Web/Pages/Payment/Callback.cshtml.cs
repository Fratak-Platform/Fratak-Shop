using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services.Payments;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Payment;

/// <summary>Gateway return URL: verifies the payment and updates the order.</summary>
public class CallbackModel(IOrderService orderService, IPaymentGateway gateway, ILogger<CallbackModel> logger) : ShopPageModel
{
    public async Task<IActionResult> OnGetAsync(string orderNumber)
    {
        var order = await orderService.GetOrderByNumberAsync(orderNumber);
        if (order is null || order.UserId != CurrentUserId)
            return NotFound();

        if (order.PaymentStatus == PaymentStatus.Paid)
            return RedirectToPage("/CheckoutComplete", new { orderNumber });

        var verify = await gateway.VerifyAsync(order, Request.Query);

        if (!verify.Succeeded)
        {
            await orderService.ChangePaymentStatusAsync(order.Id, PaymentStatus.Failed);
            ErrorMessage = verify.Error ?? "پرداخت ناموفق بود.";
            return RedirectToPage("/CheckoutFailed", new { orderNumber });
        }

        await orderService.ChangePaymentStatusAsync(order.Id, PaymentStatus.Paid, verify.TransactionId);

        var statusResult = await orderService.ChangeOrderStatusAsync(order.Id, OrderStatus.Confirmed);
        if (statusResult.Status != Core.Utilities.EnOperationResultStatus.Success)
            logger.LogWarning("Order {OrderNumber} paid but could not be confirmed: {Message}", orderNumber, statusResult.Message);

        SuccessMessage = "پرداخت با موفقیت انجام شد.";
        return RedirectToPage("/CheckoutComplete", new { orderNumber });
    }
}
