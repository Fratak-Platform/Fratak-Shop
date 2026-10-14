using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Orders;

public interface IOrderItemService
{
    /// <summary>افزودن قلم به سفارش با قیمت جاری محصول و رزرو موجودی انبار.</summary>
    Task<OperationResult> AddItemAsync(AddOrderItemCommand command);

    /// <summary>تغییر تعداد قلم سفارش؛ موجودی انبار و مبالغ سفارش اصلاح می‌شود.</summary>
    Task<OperationResult> EditItemAsync(EditOrderItemCommand command);

    /// <summary>حذف قلم از سفارش و برگرداندن موجودی آن به انبار.</summary>
    Task<OperationResult> RemoveItemAsync(Guid id);

    Task<OrderItemDto?> GetItemByIdAsync(Guid id);
    Task<List<OrderItemDto>> GetItemsByOrderAsync(Guid orderId);
}
