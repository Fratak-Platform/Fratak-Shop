using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.Services.Orders;

public interface IOrderService
{
    /// <summary>
    /// ثبت سفارش همراه با اقلام آن. قیمت‌ها، تخفیف‌ها و موجودی انبار از خود محصول
    /// خوانده می‌شود و شماره سفارش به‌صورت خودکار ساخته می‌شود.
    /// </summary>
    Task<OperationResult<OrderDto>> CreateOrderAsync(CreateOrderCommand command);

    Task<OperationResult> EditOrderAsync(EditOrderCommand command);
    Task<OperationResult> DeleteOrderAsync(Guid id);

    Task<OperationResult> ChangeOrderStatusAsync(Guid id, OrderStatus status);
    Task<OperationResult> ChangePaymentStatusAsync(Guid id, PaymentStatus paymentStatus, string? transactionId = null);
    /// <summary>
    /// Customer cancels own order (only before preparation starts). Reserved stock is
    /// released and a paid amount is refunded to the customer's wallet.
    /// </summary>
    Task<OperationResult> CancelOrderByCustomerAsync(Guid orderId, Guid userId, string? reason);

    /// <summary>Admin cancels (or registers a return of) an order; paid amounts go to the wallet.</summary>
    Task<OperationResult> CancelOrderByAdminAsync(Guid orderId, OrderStatus status, string? reason);

    /// <summary>Cancels online orders that were not paid before their deadline; returns the count.</summary>
    Task<int> ExpireUnpaidOrdersAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an order as paid after a verified gateway payment (atomic). When the order
    /// can no longer accept the payment the caller must refund it (to the wallet).
    /// </summary>
    Task<OrderPaymentOutcome> ApplyOnlinePaymentAsync(Guid orderId, decimal amount, string referenceId);

    Task<OperationResult> SetTrackingCodeAsync(Guid id, string trackingCode);

    /// <summary>مبالغ سفارش را از روی اقلام آن بازمحاسبه می‌کند.</summary>
    Task<OperationResult> RecalculateTotalsAsync(Guid orderId);

    Task<OrderFilterResult> GetOrdersByFilterAsync(OrderFilterParams filterParams);
    Task<OrderDto?> GetOrderByIdAsync(Guid id);
    Task<OrderDto?> GetOrderByNumberAsync(string orderNumber);
    Task<List<OrderFilterData>> GetOrdersByUserAsync(Guid userId);
    Task<List<OrderFilterData>> GetOrdersAsync();
}

public enum OrderPaymentOutcome
{
    Paid = 1,
    AlreadyPaid = 2,
    NotPayable = 3,
}
