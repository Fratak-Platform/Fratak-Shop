using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.Services.Orders;

public interface IOrderItemService
{
    Task<OperationResult> AddItemAsync(OrderItem item);
    Task<OperationResult> UpdateItemAsync(OrderItem item);
    Task<OperationResult> RemoveItemAsync(Guid id);
    Task<OrderItem?> GetItemByIdAsync(Guid id);
    Task<List<OrderItem>> GetItemsByOrderAsync(Guid orderId);
}
