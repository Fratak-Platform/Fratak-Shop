using Fratak_Shop.Core.DTOs.Shopping.Command;
using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Shopping;

public interface ICartService
{
    /// <summary>ایجاد سبد خرید کاربر یا مهمان؛ برای هر کاربر/سشن فقط یک سبد فعال.</summary>
    Task<OperationResult> CreateCartAsync(CreateCartCommand command);

    Task<OperationResult> AddItemAsync(AddCartItemCommand command);
    Task<OperationResult> UpdateItemAsync(UpdateCartItemCommand command);
    Task<OperationResult> RemoveItemAsync(RemoveCartItemCommand command);
    Task<OperationResult> ClearCartAsync(Guid cartId);

    /// <summary>
    /// انتقال اقلام سبد خرید مهمان به سبد کاربر پس از ورود؛ اگر کاربر سبد نداشته
    /// باشد، همان سبد مهمان به نام او ثبت می‌شود.
    /// </summary>
    Task<OperationResult> MergeGuestCartAsync(string sessionKey, Guid userId);

    /// <summary>
    /// سبد خرید کاربر/مهمان را برمی‌گرداند و در صورت نبودن، سبد خالی می‌سازد.
    /// </summary>
    Task<CartDto?> GetOrCreateCartAsync(Guid? userId, string? sessionKey);

    Task<CartDto?> GetCartByIdAsync(Guid cartId);
    Task<CartDto?> GetCartByUserAsync(Guid userId);
    Task<CartDto?> GetCartBySessionAsync(string sessionKey);
    Task<List<CartItemDto>> GetItemsAsync(Guid cartId);
}
