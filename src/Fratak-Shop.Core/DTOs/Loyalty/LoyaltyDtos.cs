using Fratak_Shop.Core.DTOs.Marketing;
using Fratak_Shop.Data.Entities.Loyalty;

namespace Fratak_Shop.Core.DTOs.Loyalty;

public class LoyaltyRuleDto
{
    public Guid Id { get; set; }
    public LoyaltyAction Action { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public int Points { get; set; }
    public int PointsPerStep { get; set; }
    public decimal AmountStep { get; set; }
    public decimal MinOrderAmount { get; set; }
    public int? MaxPointsPerEvent { get; set; }
    public int? MaxTimesPerUser { get; set; }

    /// <summary>"۱۰۰ امتیاز" or "۱ امتیاز به ازای هر ۱۰٬۰۰۰ تومان".</summary>
    public string PointsText => AmountStep > 0 && PointsPerStep > 0
        ? $"{PointsPerStep:N0} امتیاز به ازای هر {AmountStep:N0} تومان"
        : $"{Points:N0} امتیاز";
}

public class LoyaltyTierDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public int MinLifetimePoints { get; set; }
    public decimal PointsMultiplier { get; set; }
    public string? Color { get; set; }
    public string? Benefits { get; set; }
}

public class LoyaltyRewardDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public LoyaltyRewardType Type { get; set; }
    public int PointsCost { get; set; }
    public decimal Value { get; set; }
    public decimal? MaxDiscountAmount { get; set; }
    public decimal MinOrderAmount { get; set; }
    public int CouponValidDays { get; set; }
    public Guid? MinTierId { get; set; }
    public string? MinTierName { get; set; }
    public int MinTierPoints { get; set; }
}

/// <summary>Active rules, levels and rewards (cached).</summary>
public class LoyaltyProgramDto
{
    public List<LoyaltyRuleDto> Rules { get; set; } = [];
    public List<LoyaltyTierDto> Tiers { get; set; } = [];
    public List<LoyaltyRewardDto> Rewards { get; set; } = [];

    public LoyaltyTierDto? TierFor(int lifetimePoints) =>
        Tiers.Where(t => t.MinLifetimePoints <= lifetimePoints).MaxBy(t => t.MinLifetimePoints);

    public LoyaltyTierDto? NextTier(int lifetimePoints) =>
        Tiers.Where(t => t.MinLifetimePoints > lifetimePoints).MinBy(t => t.MinLifetimePoints);
}

public class LoyaltyTransactionDto
{
    public Guid Id { get; set; }
    public LoyaltyTransactionType Type { get; set; }
    public LoyaltyAction? Action { get; set; }
    public int Points { get; set; }
    public int BalanceAfter { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public class LoyaltySummaryDto
{
    public int Points { get; set; }
    public int LifetimePoints { get; set; }
    public LoyaltyTierDto? Tier { get; set; }
    public LoyaltyTierDto? NextTier { get; set; }

    /// <summary>0–100: progress toward the next level.</summary>
    public int ProgressPercent { get; set; }

    public string? ReferralCode { get; set; }
    public int ReferralCount { get; set; }

    /// <summary>Points that expire within the next 30 days.</summary>
    public int ExpiringSoon { get; set; }

    public LoyaltyProgramDto Program { get; set; } = new();
    public List<LoyaltyTransactionDto> Transactions { get; set; } = [];
    public List<CouponDto> Coupons { get; set; } = [];

    public bool CanRedeem(LoyaltyRewardDto reward) =>
        Points >= reward.PointsCost && (reward.MinTierId is null || LifetimePoints >= reward.MinTierPoints);
}
