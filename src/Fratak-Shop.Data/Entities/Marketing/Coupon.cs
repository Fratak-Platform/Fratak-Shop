using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Marketing;

/// <summary>
/// Discount code with business rules, fully managed from the admin panel.
/// Example: "8% off for orders between 2,000,000 and 50,000,000 Toman, at most
/// 1,000,000 Toman discount, once per customer, only for mobile phones".
/// </summary>
public class Coupon : BaseEntity
{
    /// <summary>Code typed by the customer (stored upper case, unique).</summary>
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    /// <summary>Terms shown to the customer (e.g. in the coupon list of the club page).</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    public CouponType Type { get; set; }

    /// <summary>Percent (1–100) for <see cref="CouponType.Percentage"/>, Toman for <see cref="CouponType.FixedAmount"/>.</summary>
    public decimal Value { get; set; }

    /// <summary>Upper limit of the discount (Toman); null = unlimited. Mostly used with percentages.</summary>
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>The eligible amount of the cart must be at least this much (Toman, 0 = no minimum).</summary>
    public decimal MinOrderAmount { get; set; }

    /// <summary>The eligible amount of the cart may not exceed this (Toman); null = no limit.</summary>
    public decimal? MaxOrderAmount { get; set; }

    /// <summary>Minimum number of items (quantity) of eligible products in the cart (0 = no rule).</summary>
    public int MinItemCount { get; set; }

    public DateTimeOffset? StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>Total number of uses for all customers; null = unlimited.</summary>
    public int? UsageLimit { get; set; }

    /// <summary>Number of uses per customer; null = unlimited.</summary>
    public int? UsageLimitPerUser { get; set; } = 1;

    /// <summary>Number of placed (not cancelled) orders that used the coupon.</summary>
    public int UsedCount { get; set; }

    /// <summary>Only for the customer's first order.</summary>
    public bool FirstOrderOnly { get; set; }

    /// <summary>Products that already have a price discount don't count toward the coupon.</summary>
    public bool ExcludeDiscountedProducts { get; set; }

    /// <summary>Personal coupon: only this customer can use it (loyalty rewards, apologies, ...).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Shown in the public "available coupons" list of the customer club.</summary>
    public bool IsPublic { get; set; }

    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>Optional scope (categories / products / brands to include or exclude).</summary>
    public List<CouponTarget> Targets { get; set; } = [];

    public List<CouponUsage> Usages { get; set; } = [];
}

/// <summary>
/// Scope rule of a coupon. When a coupon has "include" rules only the matching
/// products count; "exclude" rules always remove products from the eligible amount.
/// </summary>
public class CouponTarget : BaseEntity
{
    public Guid CouponId { get; set; }

    public CouponTargetType TargetType { get; set; }

    /// <summary>Id of the category (sub categories included), product or brand.</summary>
    public Guid TargetId { get; set; }

    public bool IsExcluded { get; set; }

    [ForeignKey(nameof(CouponId))]
    public Coupon Coupon { get; set; } = null!;
}

/// <summary>One use of a coupon by an order (reverted when the order is cancelled).</summary>
public class CouponUsage : BaseEntity
{
    public Guid CouponId { get; set; }

    public Guid? UserId { get; set; }

    public Guid OrderId { get; set; }

    public decimal DiscountAmount { get; set; }

    public bool IsReverted { get; set; }

    public DateTimeOffset? RevertedAt { get; set; }

    [ForeignKey(nameof(CouponId))]
    public Coupon Coupon { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;
}

public enum CouponType
{
    None = 0,
    Percentage = 1,
    FixedAmount = 2,
    FreeShipping = 3,
}

public enum CouponTargetType
{
    None = 0,
    Category = 1,
    Product = 2,
    Brand = 3,
}
