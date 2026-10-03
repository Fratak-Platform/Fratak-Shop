using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Marketing;

/// <summary>
/// Discount code applied in the cart/checkout. The shop supports both percentage
/// and fixed-amount codes with an optional usage limit, per-user limit and a
/// validity window, which covers every usual campaign of a storefront.
/// </summary>
public class Coupon : BaseEntity
{
    /// <summary>
    /// Code the customer types in the checkout (stored upper-case).
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = null!;

    [MaxLength(250)]
    public string? Title { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    public CouponDiscountType DiscountType { get; set; }

    /// <summary>
    /// Percentage (1..100) when <see cref="DiscountType"/> is Percent,
    /// otherwise the fixed amount in Toman.
    /// </summary>
    public decimal Value { get; set; }

    public decimal? MinOrderAmount { get; set; }

    /// <summary>
    /// Caps the discount of percentage coupons.
    /// </summary>
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>
    /// Total number of times the code may be used; null means unlimited.
    /// </summary>
    public int? UsageLimit { get; set; }

    public int UsedCount { get; set; }

    /// <summary>
    /// How many times a single user may use the code.
    /// </summary>
    public int PerUserLimit { get; set; } = 1;

    public DateTimeOffset? StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public bool IsActive { get; set; } = true;

    public List<CouponUsage> Usages { get; set; } = [];
}

public enum CouponDiscountType
{
    None = 0,
    Percent = 1,
    FixedAmount = 2,
}

/// <summary>
/// Redemption record of a coupon, used for the usage limits and for the
/// campaign report in the admin panel.
/// </summary>
public class CouponUsage : BaseEntity
{
    public Guid CouponId { get; set; }

    public Guid? UserId { get; set; }

    public Guid OrderId { get; set; }

    public decimal DiscountAmount { get; set; }

    public Coupon Coupon { get; set; } = null!;

    public User? User { get; set; }

    public Order Order { get; set; } = null!;
}
