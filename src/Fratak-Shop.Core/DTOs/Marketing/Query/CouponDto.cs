using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Marketing;

namespace Fratak_Shop.Core.DTOs.Marketing.Query;

public class CouponDto : BaseDto
{
    public string Code { get; set; } = null!;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public CouponDiscountType DiscountType { get; set; }
    public decimal Value { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public int? UsageLimit { get; set; }
    public int UsedCount { get; set; }
    public int PerUserLimit { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public bool IsActive { get; set; }

    public bool IsExpired => EndsAt.HasValue && EndsAt.Value < DateTimeOffset.UtcNow;
    public bool IsStarted => !StartsAt.HasValue || StartsAt.Value <= DateTimeOffset.UtcNow;
    public bool IsExhausted => UsageLimit.HasValue && UsedCount >= UsageLimit.Value;
    public string StatusTitle => !IsActive ? "غیرفعال" : IsExpired ? "منقضی" : !IsStarted ? "زمان‌بندی‌شده" : IsExhausted ? "تمام‌شده" : "فعال";
}

public class CouponFilterParams : BaseFilterParam
{
    public string? Search { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsExpired { get; set; }
}

public class CouponFilterResult : BaseFilter<CouponDto, CouponFilterParams>;

/// <summary>نتیجه بررسی یک کد تخفیف روی سبد خرید.</summary>
public class CouponValidationResult
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public decimal DiscountAmount { get; set; }
    public Guid? CouponId { get; set; }
    public string? Code { get; set; }
}

public class ApplyCouponCommand
{
    public string Code { get; set; } = null!;
    public Guid? UserId { get; set; }
    public string? SessionKey { get; set; }
}
