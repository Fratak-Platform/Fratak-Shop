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
}
