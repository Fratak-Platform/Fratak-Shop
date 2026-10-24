using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Loyalty;

/// <summary>
/// How many club points an action earns. Everything is editable in the admin panel:
/// fixed points per action, points per spent amount for purchases, caps and how
/// often a customer can earn the reward.
/// </summary>
public class LoyaltyRule : BaseEntity
{
    public LoyaltyAction Action { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Fixed points of the action.</summary>
    public int Points { get; set; }

    /// <summary>
    /// Purchase rules: points per <see cref="AmountStep"/> Toman of the paid amount
    /// (e.g. 1 point per 10,000 Toman). Ignored when <see cref="AmountStep"/> is 0.
    /// </summary>
    public int PointsPerStep { get; set; }

    public decimal AmountStep { get; set; }

    /// <summary>Purchases below this amount earn nothing (0 = no minimum).</summary>
    public decimal MinOrderAmount { get; set; }

    /// <summary>Upper limit of points for one event; null = unlimited.</summary>
    public int? MaxPointsPerEvent { get; set; }

    /// <summary>How many times a customer can earn this rule; null = unlimited.</summary>
    public int? MaxTimesPerUser { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }
}

/// <summary>
/// Club level reached by lifetime points. Higher levels multiply the earned points
/// (e.g. gold = ×1.5) and can show their own benefits.
/// </summary>
public class LoyaltyTier : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>Lifetime points needed to reach the level.</summary>
    public int MinLifetimePoints { get; set; }

    /// <summary>Multiplier applied to earned points (1.00 = normal).</summary>
    public decimal PointsMultiplier { get; set; } = 1;

    /// <summary>Hex colour of the badge, e.g. #f59e0b.</summary>
    [MaxLength(20)]
    public string? Color { get; set; }

    [MaxLength(1000)]
    public string? Benefits { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }
}

/// <summary>Something a customer can buy with points: wallet credit or a personal coupon.</summary>
public class LoyaltyReward : BaseEntity
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public LoyaltyRewardType Type { get; set; }

    public int PointsCost { get; set; }

    /// <summary>Toman (wallet credit / fixed coupon) or percent (percentage coupon).</summary>
    public decimal Value { get; set; }

    /// <summary>Discount cap of a percentage coupon (Toman).</summary>
    public decimal? MaxDiscountAmount { get; set; }

    /// <summary>Minimum order amount of the generated coupon.</summary>
    public decimal MinOrderAmount { get; set; }

    /// <summary>Validity of the generated coupon in days.</summary>
    public int CouponValidDays { get; set; } = 30;

    /// <summary>Only customers of this level (or higher) can redeem; null = everyone.</summary>
    public Guid? MinTierId { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    [ForeignKey(nameof(MinTierId))]
    public LoyaltyTier? MinTier { get; set; }
}

/// <summary>
/// Append-only points ledger. <see cref="ReferenceKey"/> is unique per user so the same
/// event (e.g. "order:{id}") can never be rewarded twice.
/// </summary>
public class LoyaltyTransaction : BaseEntity
{
    public Guid UserId { get; set; }

    public LoyaltyTransactionType Type { get; set; }

    public LoyaltyAction? Action { get; set; }

    /// <summary>Signed points: positive = earned, negative = spent / reverted / expired.</summary>
    public int Points { get; set; }

    public int BalanceAfter { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(100)]
    public string? ReferenceKey { get; set; }

    public Guid? OrderId { get; set; }

    /// <summary>Earned points expire at this moment (null = never).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}

public enum LoyaltyAction
{
    None = 0,
    Registration = 1,       // ثبت‌نام
    CompleteProfile = 2,    // تکمیل اطلاعات حساب
    FirstOrder = 3,         // اولین خرید
    Purchase = 4,           // هر خرید (بر اساس مبلغ)
    ProductReview = 5,      // ثبت دیدگاه تأییدشده
    Referral = 6,           // معرفی دوستان (پس از اولین خرید دوست)
    ReferralSignup = 7,     // ثبت‌نام با کد معرف (هدیه به کاربر جدید)
    Birthday = 8,           // هدیه تولد (سالی یک بار)
}

public enum LoyaltyTransactionType
{
    None = 0,
    Earn = 1,
    Redeem = 2,
    Revert = 3,
    Expire = 4,
    AdminAdjust = 5,
}

public enum LoyaltyRewardType
{
    None = 0,
    WalletCredit = 1,
    PercentageCoupon = 2,
    FixedCoupon = 3,
    FreeShippingCoupon = 4,
}
