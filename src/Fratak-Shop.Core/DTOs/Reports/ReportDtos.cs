using Fratak_Shop.Data.Entities.Loyalty;

namespace Fratak_Shop.Core.DTOs.Reports;

/// <summary>
/// Report period (UTC). Orders count when created inside the period and they are "realized":
/// not cancelled / returned and paid (online, wallet) or delivered (cash on delivery).
/// </summary>
public record ReportPeriod(DateTimeOffset? From = null, DateTimeOffset? To = null)
{
    public static ReportPeriod LastDays(int days) => new(DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 3650)), null);
}

/// <summary>Everything the shop gave away as discount in a period.</summary>
public class DiscountReportDto
{
    public ReportPeriod Period { get; set; } = new();
    public int OrderCount { get; set; }

    /// <summary>Items at list price (before any discount).</summary>
    public decimal GrossSales { get; set; }

    /// <summary>Product discounts (list price − sale price).</summary>
    public decimal ProductDiscount { get; set; }

    /// <summary>Coupon discounts (free shipping included).</summary>
    public decimal CouponDiscount { get; set; }

    /// <summary>Other order-level discounts (manual).</summary>
    public decimal OtherDiscount { get; set; }

    public decimal TotalDiscount => ProductDiscount + CouponDiscount + OtherDiscount;

    /// <summary>Promotional (club / campaign) wallet credit spent on orders, net of refunds.</summary>
    public decimal BonusCreditSpent { get; set; }

    public decimal ShippingCollected { get; set; }

    /// <summary>Amount the customers paid.</summary>
    public decimal NetSales { get; set; }

    /// <summary>Share of the gross sales given away as discount, percent.</summary>
    public decimal DiscountRate => GrossSales == 0 ? 0 : Math.Round(TotalDiscount * 100 / GrossSales, 1);

    public int OrdersWithCoupon { get; set; }
    public List<CouponUsageRow> Coupons { get; set; } = [];
}

public record CouponUsageRow(string Code, int Orders, decimal Discount, decimal Sales);

/// <summary>Customer-club figures: points flow, outstanding liability, members.</summary>
public class LoyaltyReportDto
{
    public ReportPeriod Period { get; set; } = new();
    public int PointsEarned { get; set; }
    public int PointsRedeemed { get; set; }
    public int PointsExpired { get; set; }
    public int PointsReverted { get; set; }
    public int PointsAdjusted { get; set; }
    public List<LoyaltyActionRow> EarnedByAction { get; set; } = [];

    /// <summary>Points customers hold right now (all time).</summary>
    public long OutstandingPoints { get; set; }

    /// <summary>Toman value of one point by the best wallet-credit reward (0 = no such reward).</summary>
    public decimal PointValue { get; set; }

    public decimal OutstandingPointsValue => Math.Round(OutstandingPoints * PointValue, 0);

    /// <summary>Non-withdrawable wallet credit customers hold right now.</summary>
    public decimal OutstandingBonusCredit { get; set; }

    /// <summary>Promotional credit granted in the period (club rewards, campaigns).</summary>
    public decimal BonusCreditGranted { get; set; }

    public int MembersWithPoints { get; set; }
    public int ActiveMembers { get; set; }
}

public record LoyaltyActionRow(LoyaltyAction Action, int Points, int Count);

/// <summary>Gross profit by the purchase price snapshot stored on every order item.</summary>
public class ProfitReportDto
{
    public ReportPeriod Period { get; set; } = new();
    public int OrderCount { get; set; }

    /// <summary>Items at sale price.</summary>
    public decimal ItemsRevenue { get; set; }

    /// <summary>Order-level discounts (coupons, manual).</summary>
    public decimal OrderDiscounts { get; set; }

    public decimal NetRevenue => ItemsRevenue - OrderDiscounts;

    /// <summary>Purchase cost of the items that have a cost price.</summary>
    public decimal Cost { get; set; }

    /// <summary>Revenue of items without a cost price (excluded from profit).</summary>
    public decimal RevenueWithoutCost { get; set; }

    public int ItemsWithoutCost { get; set; }

    /// <summary>Revenue − order discounts (proportionally for costed items) − cost.</summary>
    public decimal GrossProfit { get; set; }

    public decimal MarginPercent { get; set; }

    public decimal ShippingCollected { get; set; }

    public List<ProductProfitRow> TopProducts { get; set; } = [];
}

public record ProductProfitRow(Guid? ProductId, string ProductName, int Quantity, decimal Revenue, decimal Cost, decimal Profit);
