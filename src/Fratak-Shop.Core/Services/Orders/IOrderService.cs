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
    Task<OperationResult> SetTrackingCodeAsync(Guid id, string trackingCode);

    /// <summary>مبالغ سفارش را از روی اقلام آن بازمحاسبه می‌کند.</summary>
    Task<OperationResult> RecalculateTotalsAsync(Guid orderId);

    Task<OrderFilterResult> GetOrdersByFilterAsync(OrderFilterParams filterParams);
    Task<OrderDto?> GetOrderByIdAsync(Guid id);
    Task<OrderDto?> GetOrderByNumberAsync(string orderNumber);
    Task<List<OrderFilterData>> GetOrdersByUserAsync(Guid userId);
    Task<List<OrderFilterData>> GetOrdersAsync();
}
