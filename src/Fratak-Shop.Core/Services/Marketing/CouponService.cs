using Fratak_Shop.Core.DTOs.Marketing.Command;
using Fratak_Shop.Core.DTOs.Marketing.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Marketing;

public class CouponService(FratakShopDbContext context) : ICouponService
{
    public async Task<CouponValidationResult> ValidateAsync(string code, decimal subtotal, Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Invalid("کد تخفیف را وارد کنید.");

        var normalized = code.Trim().ToUpperInvariant();
        var coupon = await context.Coupons.FirstOrDefaultAsync(item => item.Code == normalized, cancellationToken);
        if (coupon is null)
            return Invalid("کد تخفیف وارد‌شده معتبر نیست.");

        if (!coupon.IsActive)
            return Invalid("این کد تخفیف غیرفعال است.");

        var now = DateTimeOffset.UtcNow;
        if (coupon.StartsAt.HasValue && coupon.StartsAt.Value > now)
            return Invalid("زمان استفاده از این کد تخفیف فرا نرسیده است.");

        if (coupon.EndsAt.HasValue && coupon.EndsAt.Value < now)
            return Invalid("مهلت استفاده از این کد تخفیف به پایان رسیده است.");

        if (coupon.UsageLimit.HasValue && coupon.UsedCount >= coupon.UsageLimit.Value)
            return Invalid("ظرفیت استفاده از این کد تخفیف تکمیل شده است.");

        if (coupon.MinOrderAmount.HasValue && subtotal < coupon.MinOrderAmount.Value)
            return Invalid($"حداقل مبلغ سفارش برای استفاده از این کد {coupon.MinOrderAmount.Value.ToToman()} است.");

        if (userId.HasValue && coupon.PerUserLimit > 0)
        {
            var usageCount = await context.CouponUsages
                .CountAsync(usage => usage.CouponId == coupon.Id && usage.UserId == userId.Value, cancellationToken);

            if (usageCount >= coupon.PerUserLimit)
                return Invalid("شما قبلاً از این کد تخفیف استفاده کرده‌اید.");
        }

        decimal discount = coupon.DiscountType switch
        {
            CouponDiscountType.Percent => Math.Round(subtotal * coupon.Value / 100m),
            CouponDiscountType.FixedAmount => coupon.Value,
            _ => 0,
        };

        if (coupon.MaxDiscountAmount.HasValue && discount > coupon.MaxDiscountAmount.Value)
            discount = coupon.MaxDiscountAmount.Value;

        if (discount > subtotal)
            discount = subtotal;

        if (discount <= 0)
            return Invalid("این کد تخفیف مبلغ قابل اعمالی ندارد.");

        return new CouponValidationResult
        {
            IsValid = true,
            Message = $"کد تخفیف «{coupon.Code}» اعمال شد.",
            DiscountAmount = discount,
            CouponId = coupon.Id,
            Code = coupon.Code,
        };
    }

    public async Task<OperationResult> RegisterUsageAsync(Guid couponId, Guid orderId, Guid? userId, decimal discountAmount,
        CancellationToken cancellationToken = default)
    {
        var coupon = await context.Coupons.AsTracking().FirstOrDefaultAsync(item => item.Id == couponId, cancellationToken);
        if (coupon is null)
            return OperationResult.NotFound("کد تخفیف یافت نشد.");

        context.CouponUsages.Add(new CouponUsage
        {
            CouponId = couponId,
            OrderId = orderId,
            UserId = userId,
            DiscountAmount = discountAmount,
        });

        coupon.UsedCount++;

        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("استفاده از کد تخفیف ثبت شد.");
    }

    public async Task<List<CouponDto>> GetActiveAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var coupons = await context.Coupons
            .Where(coupon => coupon.IsActive &&
                             (coupon.StartsAt == null || coupon.StartsAt <= now) &&
                             (coupon.EndsAt == null || coupon.EndsAt >= now) &&
                             (coupon.UsageLimit == null || coupon.UsedCount < coupon.UsageLimit))
            .OrderByDescending(coupon => coupon.CreatedAt)
            .ToListAsync();

        return coupons.Select(Map).ToList();
    }

    public async Task<CouponFilterResult> GetFilterAsync(CouponFilterParams filterParams)
    {
        var query = context.Coupons.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var term = filterParams.Search.Trim();
            query = query.Where(coupon => coupon.Code.Contains(term) ||
                                          (coupon.Title != null && coupon.Title.Contains(term)));
        }

        if (filterParams.IsActive.HasValue)
            query = query.Where(coupon => coupon.IsActive == filterParams.IsActive.Value);

        if (filterParams.IsExpired.HasValue)
        {
            var now = DateTimeOffset.UtcNow;
            query = filterParams.IsExpired.Value
                ? query.Where(coupon => coupon.EndsAt != null && coupon.EndsAt < now)
                : query.Where(coupon => coupon.EndsAt == null || coupon.EndsAt >= now);
        }

        var total = await query.CountAsync();
        var skip = (filterParams.PageId - 1) * filterParams.Take;

        var items = await query
            .OrderByDescending(coupon => coupon.CreatedAt)
            .Skip(skip)
            .Take(filterParams.Take)
            .ToListAsync();

        var result = new CouponFilterResult
        {
            Data = items.Select(Map).ToList(),
            FilterParams = filterParams,
        };
        result.GeneratePaging(total, filterParams.Take, filterParams.PageId);
        return result;
    }

    public async Task<CouponDto?> GetAsync(Guid id)
    {
        var coupon = await context.Coupons.FirstOrDefaultAsync(item => item.Id == id);
        return coupon is null ? null : Map(coupon);
    }

    public async Task<OperationResult> SaveAsync(SaveCouponCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Code))
            return OperationResult.Error("کد تخفیف را وارد کنید.");

        var code = command.Code.Trim().ToUpperInvariant().Replace(" ", string.Empty);

        if (command.DiscountType == CouponDiscountType.None)
            return OperationResult.Error("نوع تخفیف را انتخاب کنید.");

        if (command.Value <= 0)
            return OperationResult.Error("مقدار تخفیف باید بزرگ‌تر از صفر باشد.");

        if (command.DiscountType == CouponDiscountType.Percent && command.Value > 100)
            return OperationResult.Error("درصد تخفیف نمی‌تواند بیشتر از ۱۰۰ باشد.");

        if (command.StartsAt.HasValue && command.EndsAt.HasValue && command.StartsAt > command.EndsAt)
            return OperationResult.Error("تاریخ پایان باید بعد از تاریخ شروع باشد.");

        if (command.Id.HasValue)
        {
            var coupon = await context.Coupons.AsTracking().FirstOrDefaultAsync(item => item.Id == command.Id.Value);
            if (coupon is null)
                return OperationResult.NotFound("کد تخفیف مورد نظر یافت نشد.");

            var duplicate = await context.Coupons.AnyAsync(item => item.Id != command.Id.Value && item.Code == code);
            if (duplicate)
                return OperationResult.Error("این کد تخفیف قبلاً ثبت شده است.");

            coupon.Code = code;
            coupon.Title = command.Title;
            coupon.Description = command.Description;
            coupon.DiscountType = command.DiscountType;
            coupon.Value = command.Value;
            coupon.MinOrderAmount = command.MinOrderAmount;
            coupon.MaxDiscountAmount = command.MaxDiscountAmount;
            coupon.UsageLimit = command.UsageLimit;
            coupon.PerUserLimit = command.PerUserLimit;
            coupon.StartsAt = command.StartsAt;
            coupon.EndsAt = command.EndsAt;
            coupon.IsActive = command.IsActive;

            await context.SaveChangesAsync();
            return OperationResult.Success("کد تخفیف ویرایش شد.");
        }

        var exists = await context.Coupons.AnyAsync(item => item.Code == code);
        if (exists)
            return OperationResult.Error("این کد تخفیف قبلاً ثبت شده است.");

        context.Coupons.Add(new Coupon
        {
            Code = code,
            Title = command.Title,
            Description = command.Description,
            DiscountType = command.DiscountType,
            Value = command.Value,
            MinOrderAmount = command.MinOrderAmount,
            MaxDiscountAmount = command.MaxDiscountAmount,
            UsageLimit = command.UsageLimit,
            PerUserLimit = command.PerUserLimit,
            StartsAt = command.StartsAt,
            EndsAt = command.EndsAt,
            IsActive = command.IsActive,
        });

        await context.SaveChangesAsync();
        return OperationResult.Success("کد تخفیف ثبت شد.");
    }

    public async Task<OperationResult> ToggleActiveAsync(Guid id)
    {
        var coupon = await context.Coupons.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (coupon is null)
            return OperationResult.NotFound("کد تخفیف مورد نظر یافت نشد.");

        coupon.IsActive = !coupon.IsActive;
        await context.SaveChangesAsync();

        return OperationResult.Success(coupon.IsActive ? "کد تخفیف فعال شد." : "کد تخفیف غیرفعال شد.");
    }

    public async Task<OperationResult> DeleteAsync(Guid id)
    {
        var coupon = await context.Coupons.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (coupon is null)
            return OperationResult.NotFound("کد تخفیف مورد نظر یافت نشد.");

        var used = await context.CouponUsages.AnyAsync(usage => usage.CouponId == id);
        if (used)
        {
            coupon.IsActive = false;
            await context.SaveChangesAsync();
            return OperationResult.Success("این کد در سفارش‌ها استفاده شده است؛ به‌جای حذف غیرفعال شد.");
        }

        context.Coupons.Remove(coupon);
        await context.SaveChangesAsync();
        return OperationResult.Success("کد تخفیف حذف شد.");
    }

    public async Task<Coupon?> GetEntityByCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var normalized = code.Trim().ToUpperInvariant();
        return await context.Coupons.FirstOrDefaultAsync(item => item.Code == normalized);
    }

    private static CouponValidationResult Invalid(string message) => new() { IsValid = false, Message = message };

    private static CouponDto Map(Coupon coupon) => new()
    {
        Id = coupon.Id,
        CreatedAt = coupon.CreatedAt,
        UpdatedAt = coupon.UpdatedAt,
        Code = coupon.Code,
        Title = coupon.Title,
        Description = coupon.Description,
        DiscountType = coupon.DiscountType,
        Value = coupon.Value,
        MinOrderAmount = coupon.MinOrderAmount,
        MaxDiscountAmount = coupon.MaxDiscountAmount,
        UsageLimit = coupon.UsageLimit,
        UsedCount = coupon.UsedCount,
        PerUserLimit = coupon.PerUserLimit,
        StartsAt = coupon.StartsAt,
        EndsAt = coupon.EndsAt,
        IsActive = coupon.IsActive,
    };
}
