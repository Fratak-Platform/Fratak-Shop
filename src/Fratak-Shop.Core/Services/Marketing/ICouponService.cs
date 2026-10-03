using Fratak_Shop.Core.DTOs.Marketing.Command;
using Fratak_Shop.Core.DTOs.Marketing.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Marketing;

namespace Fratak_Shop.Core.Services.Marketing;

/// <summary>
/// Discount codes: validation on the cart, redemption registration on checkout and
/// the full CRUD of the marketing section of the admin panel.
/// </summary>
public interface ICouponService
{
    /// <summary>بررسی کد تخفیف روی مبلغ سبد و محاسبه تخفیف.</summary>
    Task<CouponValidationResult> ValidateAsync(string code, decimal subtotal, Guid? userId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> RegisterUsageAsync(Guid couponId, Guid orderId, Guid? userId, decimal discountAmount,
        CancellationToken cancellationToken = default);

    Task<List<CouponDto>> GetActiveAsync();

    Task<CouponFilterResult> GetFilterAsync(CouponFilterParams filterParams);

    Task<CouponDto?> GetAsync(Guid id);

    Task<OperationResult> SaveAsync(SaveCouponCommand command);

    Task<OperationResult> ToggleActiveAsync(Guid id);

    Task<OperationResult> DeleteAsync(Guid id);

    Task<Coupon?> GetEntityByCodeAsync(string code);
}
