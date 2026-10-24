using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// Business rules of the order workflow shared by the storefront and the admin panel.
/// </summary>
public static class OrderRules
{
    /// <summary>
    /// Customers may cancel an order until the shop starts preparing it
    /// (registered or confirmed). From "processing" on, only the shop can cancel.
    /// </summary>
    public static bool IsCancellableByCustomer(OrderStatus status) =>
        status is OrderStatus.Pending or OrderStatus.Confirmed;

    /// <summary>The shop can cancel an order until it is handed over to the carrier.</summary>
    public static bool IsCancellableByShop(OrderStatus status) =>
        status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Processing;

    /// <summary>An unpaid online order can still be paid while it is registered and not expired.</summary>
    public static bool IsPayable(OrderStatus status, PaymentMethod method, PaymentStatus paymentStatus, DateTimeOffset? deadline, DateTimeOffset now) =>
        status == OrderStatus.Pending
        && method == PaymentMethod.OnlineGateway
        && paymentStatus is not (PaymentStatus.Paid or PaymentStatus.Refunded)
        && (deadline is null || deadline > now);
}
