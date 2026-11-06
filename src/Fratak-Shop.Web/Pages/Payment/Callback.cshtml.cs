using Fratak_Shop.Data.Entities.Payments;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services.Payments;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Payment;

/// <summary>
/// ZarinPal return URL (<c>?Authority=...&amp;Status=OK|NOK</c>). Anonymous on purpose:
/// a payment must be verified even when the customer's session expired meanwhile.
/// The transaction is found by its authority, never by user input.
/// </summary>
public class CallbackModel(IPaymentFlowService paymentFlow, Fratak_Shop.Core.Services.Orders.IStaffOrderService staffOrders) : ShopPageModel
{
    public async Task<IActionResult> OnGetAsync([FromQuery(Name = "Authority")] string? authority, [FromQuery(Name = "Status")] string? status)
    {
        var result = await paymentFlow.HandleCallbackAsync(authority, status);

        if (result.Succeeded)
            SuccessMessage = result.Message;
        else
            ErrorMessage = result.Message;

        if (result.Purpose == PaymentPurpose.WalletTopUp)
            return RedirectToPage("/Profile/Wallet");

        if (result.Purpose == PaymentPurpose.GiftCard)
            return RedirectToPage("/Profile/GiftCards");

        if (string.IsNullOrEmpty(result.OrderNumber))
            return RedirectToPage("/Index");

        if (result.IsPending)
            return RedirectToPage("/Profile/OrderDetail", new { orderNumber = result.OrderNumber });

        // سفارش ثبت‌شده در پنل (لینک پرداخت): مشتری شاید وارد حساب نباشد، (پرداخت قطعی یا ناموفق) پس به همان صفحهٔ /pay/{token} برمی‌گردد.
        var token = await staffOrders.GetTokenByOrderNumberAsync(result.OrderNumber);
        if (token is not null)
            return Redirect(Fratak_Shop.Core.Services.Orders.StaffOrderService.LinkPath(token));

        return result.Succeeded
            ? RedirectToPage("/CheckoutComplete", new { orderNumber = result.OrderNumber })
            : RedirectToPage("/CheckoutFailed", new { orderNumber = result.OrderNumber });
    }
}
