using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Shopping;

namespace Fratak_Shop.Core.Services.Shopping;

public interface ICartService
{
    Task<OperationResult> AddItemAsync(Guid cartId, Guid productId, int quantity = 1);
    Task<OperationResult> UpdateItemAsync(Guid cartId, Guid productId, int quantity);
    Task<OperationResult> RemoveItemAsync(Guid cartId, Guid productId);
    Task<Cart?> GetCartByIdAsync(Guid cartId);
    Task<Cart?> GetCartByUserAsync(Guid userId);
    Task<List<CartItem>> GetItemsAsync(Guid cartId);
}
