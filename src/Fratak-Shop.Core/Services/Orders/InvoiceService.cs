using System.Globalization;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Orders;

/// <summary>
/// Serial sales-invoice numbers. A number is issued once per order, when the order is
/// paid (online / wallet) or – for cash on delivery – delivered. Before that the
/// printable document is a pro-forma invoice (پیش‌فاکتور).
/// Numbers come from a SQL sequence, so parallel requests never get the same number.
/// </summary>
public interface IInvoiceService
{
    /// <summary>Issues the invoice number when the order is eligible; returns the (existing or new) number.</summary>
    Task<string?> EnsureIssuedAsync(Guid orderId);

    /// <summary>Whether an order in this state gets a final invoice.</summary>
    static bool IsEligible(PaymentStatus paymentStatus, OrderStatus status) =>
        status is not (OrderStatus.Cancelled or OrderStatus.Pending)
        && (paymentStatus == PaymentStatus.Paid || status == OrderStatus.Delivered);
}

public class InvoiceService(
    FratakShopDbContext context,
    IStoreSettingService settingService,
    ILogger<InvoiceService> logger) : IInvoiceService
{
    private static readonly PersianCalendar Persian = new();

    public async Task<string?> EnsureIssuedAsync(Guid orderId)
    {
        var order = await context.Orders
            .Where(o => o.Id == orderId)
            .Select(o => new { o.InvoiceNumber, o.PaymentStatus, o.Status, o.PaidAt })
            .FirstOrDefaultAsync();

        if (order is null)
            return null;

        if (order.InvoiceNumber is not null)
            return order.InvoiceNumber;

        if (!IInvoiceService.IsEligible(order.PaymentStatus, order.Status))
            return null;

        try
        {
            var settings = await settingService.GetAsync();
            var prefix = string.IsNullOrWhiteSpace(settings.InvoiceNumberPrefix) ? "INV" : settings.InvoiceNumberPrefix.Trim().ToUpperInvariant();

            // NEXT VALUE FOR can't be used inside a derived table, so the query must not be
            // composed (no Single/First on the IQueryable) – materialize it as-is.
            var serials = await context.Database
                .SqlQueryRaw<long>("SELECT NEXT VALUE FOR [" + FratakShopDbContext.InvoiceSerialSequence + "] AS [Value]")
                .ToListAsync();
            var serial = serials.Single();

            var now = DateTimeOffset.UtcNow;
            var year = Persian.GetYear(IranClock.ToLocal(order.PaidAt ?? now).DateTime);
            var number = string.Create(CultureInfo.InvariantCulture, $"{prefix}-{year}-{serial:D6}");

            // Only the first writer wins (a parallel request may have issued one meanwhile).
            var affected = await context.Orders
                .Where(o => o.Id == orderId && o.InvoiceNumber == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.InvoiceNumber, number)
                    .SetProperty(o => o.InvoiceIssuedAt, now));

            if (affected == 1)
                return number;

            return await context.Orders.Where(o => o.Id == orderId).Select(o => o.InvoiceNumber).FirstOrDefaultAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Invoice number for order {OrderId} could not be issued.", orderId);
            return null;
        }
    }
}
