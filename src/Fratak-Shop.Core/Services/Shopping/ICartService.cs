using Fratak_Shop.Core.DTOs.Shopping.Query;
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

    // ------------------------------------------------------------------- shop
    /// <summary>سبد خرید کاربر یا مهمان را پیدا می‌کند و در نبود آن می‌سازد.</summary>
    Task<Cart> GetOrCreateAsync(Guid? userId, string? sessionKey, CancellationToken cancellationToken = default);

    /// <summary>سبد خرید کامل با جمع‌ها، کد تخفیف و هزینه ارسال.</summary>
    Task<CartDto> GetCartDtoAsync(Guid? userId, string? sessionKey, string? couponCode = null,
        Guid? shippingMethodId = null, CancellationToken cancellationToken = default);

    Task<OperationResult> AddToCartAsync(Guid productId, int quantity, Guid? userId, string? sessionKey,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SetQuantityAsync(Guid productId, int quantity, Guid? userId, string? sessionKey,
        CancellationToken cancellationToken = default);

    Task<OperationResult> RemoveAsync(Guid productId, Guid? userId, string? sessionKey,
        CancellationToken cancellationToken = default);

    Task<OperationResult> ClearAsync(Guid? userId, string? sessionKey, CancellationToken cancellationToken = default);

    Task<OperationResult> ApplyCouponAsync(string code, Guid? userId, string? sessionKey,
        CancellationToken cancellationToken = default);

    Task<OperationResult> RemoveCouponAsync(Guid? userId, string? sessionKey, CancellationToken cancellationToken = default);

    /// <summary>تعداد کل اقلام سبد خرید (برای نشان هدر).</summary>
    Task<int> GetItemCountAsync(Guid? userId, string? sessionKey, CancellationToken cancellationToken = default);

    /// <summary>ادغام سبد مهمان با سبد کاربر پس از ورود به حساب.</summary>
    Task<OperationResult> MergeGuestCartAsync(string sessionKey, Guid userId, CancellationToken cancellationToken = default);
}
