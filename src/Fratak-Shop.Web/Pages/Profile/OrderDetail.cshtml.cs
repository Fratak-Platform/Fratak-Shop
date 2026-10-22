using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

public class OrderDetailModel(IOrderService orderService) : ShopPageModel
{
    public OrderDto Order { get; private set; } = null!;

    /// <summary>The customer may cancel an order until the shop confirms it.</summary>
    public bool CanCancel => Order.Status == OrderStatus.Pending && Order.PaymentStatus != PaymentStatus.Paid;

    public bool CanPay => Order.Status == OrderStatus.Pending
                          && Order.PaymentMethod == PaymentMethod.OnlineGateway
                          && Order.PaymentStatus != PaymentStatus.Paid;

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
        if (!CanCancel)
        {
            ErrorMessage = "این سفارش دیگر قابل لغو نیست؛ برای پیگیری با پشتیبانی تماس بگیرید.";
            return RedirectToPage(new { orderNumber });
        }

        var result = await orderService.ChangeOrderStatusAsync(order.Id, OrderStatus.Cancelled);
        if (Succeeded(result))
            result.Message = "سفارش شما لغو شد.";

        ShowResult(result);
        return RedirectToPage(new { orderNumber });
    }
}
