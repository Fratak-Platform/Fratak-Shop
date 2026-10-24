using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

public class OrderDetailModel(IOrderService orderService, ISiteContext site) : ShopPageModel
{
    public OrderDto Order { get; private set; } = null!;

    /// <summary>
    /// Self-cancellation is allowed until the shop starts preparing the order
    /// (and only when the store allows it). A paid amount is refunded to the wallet.
    /// </summary>
    public bool CanCancel => site.Settings.AllowCustomerCancellation && OrderRules.IsCancellableByCustomer(Order.Status);

    public bool CanPay => site.Settings.HasZarinPal
                          && OrderRules.IsPayable(Order.Status, Order.PaymentMethod, Order.PaymentStatus, Order.PaymentDeadline, DateTimeOffset.UtcNow);

    public bool WillRefundToWallet => Order.PaymentStatus == PaymentStatus.Paid;

    [BindProperty]
    [MaxLength(500, ErrorMessage = "دلیل لغو حداکثر ۵۰۰ کاراکتر است.")]
    public string? CancelReason { get; set; }

    public static readonly (OrderStatus Status, string Title)[] Steps =
    [
        (OrderStatus.Pending, "ثبت سفارش"),
        (OrderStatus.Confirmed, "تایید"),
        (OrderStatus.Processing, "آماده سازی"),
        (OrderStatus.Shipped, "ارسال"),
        (OrderStatus.Delivered, "تحویل"),
    ];

    public async Task<IActionResult> OnGetAsync(string orderNumber)
    {
        var order = await orderService.GetOrderByNumberAsync(orderNumber);
        if (order is null || order.UserId != CurrentUserId)
            return NotFound();

        Order = order;
        ViewData[PageMetaKeys.Title] = $"سفارش {order.OrderNumber}";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(string orderNumber)
    {
        var order = await orderService.GetOrderByNumberAsync(orderNumber);
        if (order is null || order.UserId != CurrentUserId)
            return NotFound();

        Order = order;
        if (!CanCancel || !ModelState.IsValid)
        {
            ErrorMessage = "این سفارش دیگر قابل لغو نیست؛ برای پیگیری با پشتیبانی تماس بگیرید.";
            return RedirectToPage(new { orderNumber });
        }

        // The service re-checks status and ownership atomically (the shop may have
        // started preparing the order meanwhile) and refunds paid amounts to the wallet.
        var reason = string.IsNullOrWhiteSpace(CancelReason) ? null : CancelReason.Trim();
        var result = await orderService.CancelOrderByCustomerAsync(order.Id, CurrentUserId!.Value, reason);
        ShowResult(result);
        return RedirectToPage(new { orderNumber });
    }
}
