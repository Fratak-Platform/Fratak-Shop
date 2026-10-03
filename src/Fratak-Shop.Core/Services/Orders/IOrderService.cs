using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.Services.Orders;

public interface IOrderService
{
    Task<OperationResult> CreateOrderAsync(Order order);
    Task<OperationResult> UpdateOrderAsync(Order order);
    Task<OperationResult> DeleteOrderAsync(Guid id);
    Task<Order?> GetOrderByIdAsync(Guid id);
    Task<List<Order>> GetOrdersByUserAsync(Guid userId);
    Task<List<Order>> GetOrdersAsync();

    // --------------------------------------------------------------- management
    /// <summary>فهرست صفحه‌بندی‌شده سفارش‌ها با فیلترهای پنل مدیریت.</summary>
    Task<OrderFilterResult> GetFilterAsync(OrderFilterParams filterParams);

    /// <summary>جزئیات کامل سفارش (اقلام، تاریخچه وضعیت، پرداخت و کارت به کارت).</summary>
    Task<OrderDto?> GetDetailAsync(Guid id);

    /// <summary>سفارش‌های یک کاربر به‌صورت مدل آماده نمایش.</summary>
    Task<List<OrderDto>> GetUserOrdersAsync(Guid userId);

    /// <summary>سفارش‌های یک کاربر بر اساس شماره پیگیری/شماره سفارش (پیگیری مهمان).</summary>
    Task<OrderDto?> TrackAsync(string orderNumber, string phoneNumber);

    Task<OperationResult> ChangeStatusAsync(ChangeOrderStatusCommand command);

    Task<OperationResult> SetTrackingAsync(SetTrackingCommand command);

    Task<OperationResult> SetAdminNoteAsync(Guid orderId, string? note);

    Task<OperationResult> CancelByCustomerAsync(Guid orderId, Guid userId, string? reason);

    /// <summary>آمار سفارش‌ها و فروش برای داشبورد پنل مدیریت.</summary>
    Task<OrderStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);
}
