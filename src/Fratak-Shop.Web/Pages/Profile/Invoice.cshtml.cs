using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>
/// Printable A4 sales invoice of an order. Paid (or delivered cash-on-delivery) orders get a
/// serial invoice number (issued once, lazily here as a fallback); others show a pro-forma
/// invoice. The PDF is generated in the browser (html2pdf, vendored) – nothing is stored.
/// <c>?print=1</c> is used by the "quick print" button of the order page (hidden iframe).
/// </summary>
public class InvoiceModel(IOrderService orderService, IInvoiceService invoiceService) : ShopPageModel
{
    public OrderDto Order { get; private set; } = null!;

    /// <summary>Final invoice (has a serial number) or pro-forma.</summary>
    public bool IsFinal => !string.IsNullOrEmpty(Order.InvoiceNumber);

    [FromQuery(Name = "print")]
    public bool AutoPrint { get; set; }

    public async Task<IActionResult> OnGetAsync(string orderNumber)
    {
        var order = await orderService.GetOrderByNumberAsync(orderNumber);
        if (order is null || order.UserId != CurrentUserId)
            return NotFound();

        if (order.InvoiceNumber is null && IInvoiceService.IsEligible(order.PaymentStatus, order.Status))
        {
            var number = await invoiceService.EnsureIssuedAsync(order.Id);
            if (number is not null)
            {
                order.InvoiceNumber = number;
                order.InvoiceIssuedAt ??= DateTimeOffset.UtcNow;
            }
        }

        Order = order;
        ViewData[PageMetaKeys.Title] = $"فاکتور سفارش {order.OrderNumber}";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        Response.Headers.CacheControl = "no-store";
        return Page();
    }
}
