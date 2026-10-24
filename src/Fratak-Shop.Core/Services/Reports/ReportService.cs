using Fratak_Shop.Core.DTOs.Reports;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Reports;

/// <summary>
/// Management reports (discounts given, customer club, profit). Data only – shown by the
/// admin panel later. All figures come from snapshots stored on the orders (prices,
/// discounts, cost price), so later product edits don't change past reports.
/// </summary>
public interface IReportService
{
    Task<DiscountReportDto> GetDiscountReportAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    Task<LoyaltyReportDto> GetLoyaltyReportAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    Task<ProfitReportDto> GetProfitReportAsync(ReportPeriod period, int topProducts = 10, CancellationToken cancellationToken = default);
}

public class ReportService(FratakShopDbContext context) : IReportService
{
    /// <summary>Orders that count as sales: not cancelled / returned, and paid or delivered (COD).</summary>
    private IQueryable<Order> RealizedOrders(ReportPeriod period)
    {
        var query = context.Orders.Where(o => o.Status != OrderStatus.Cancelled
                                              && o.Status != OrderStatus.Returned
                                              && (o.PaymentStatus == PaymentStatus.Paid || o.Status == OrderStatus.Delivered));
        if (period.From.HasValue)
            query = query.Where(o => o.CreatedAt >= period.From.Value);
        if (period.To.HasValue)
            query = query.Where(o => o.CreatedAt < period.To.Value);
        return query;
    }

    public async Task<DiscountReportDto> GetDiscountReportAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var orders = RealizedOrders(period);

        var totals = await orders
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Discount = g.Sum(o => o.DiscountAmount),
                Coupon = g.Sum(o => o.CouponDiscountAmount),
                Shipping = g.Sum(o => o.ShippingAmount),
                Payable = g.Sum(o => o.PayableAmount),
                Bonus = g.Sum(o => o.WalletBonusAmount - o.RefundedBonusAmount),
                WithCoupon = g.Count(o => o.CouponCode != null),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var items = await context.OrderItems
            .Where(i => orders.Any(o => o.Id == i.OrderId))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Gross = g.Sum(i => i.UnitPrice * i.Quantity),
                Discount = g.Sum(i => i.DiscountAmount),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var coupons = await orders
            .Where(o => o.CouponCode != null)
            .GroupBy(o => o.CouponCode!)
            .Select(g => new CouponUsageRow(g.Key, g.Count(), g.Sum(o => o.CouponDiscountAmount), g.Sum(o => o.PayableAmount)))
            .OrderByDescending(r => r.Discount)
            .Take(50)
            .ToListAsync(cancellationToken);

        return new DiscountReportDto
        {
            Period = period,
            OrderCount = totals?.Count ?? 0,
            GrossSales = items?.Gross ?? 0,
            ProductDiscount = items?.Discount ?? 0,
            CouponDiscount = totals?.Coupon ?? 0,
            OtherDiscount = Math.Max(0, (totals?.Discount ?? 0) - (totals?.Coupon ?? 0)),
            BonusCreditSpent = Math.Max(0, totals?.Bonus ?? 0),
            ShippingCollected = totals?.Shipping ?? 0,
            NetSales = totals?.Payable ?? 0,
            OrdersWithCoupon = totals?.WithCoupon ?? 0,
            Coupons = coupons,
        };
    }

    public async Task<LoyaltyReportDto> GetLoyaltyReportAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var transactions = context.LoyaltyTransactions.AsQueryable();
        if (period.From.HasValue)
            transactions = transactions.Where(t => t.CreatedAt >= period.From.Value);
        if (period.To.HasValue)
            transactions = transactions.Where(t => t.CreatedAt < period.To.Value);

        var byType = await transactions
            .GroupBy(t => t.Type)
            .Select(g => new { Type = g.Key, Points = g.Sum(t => t.Points), Users = g.Select(t => t.UserId).Distinct().Count() })
            .ToListAsync(cancellationToken);

        var byAction = await transactions
            .Where(t => t.Type == LoyaltyTransactionType.Earn && t.Action != null)
            .GroupBy(t => t.Action!.Value)
            .Select(g => new LoyaltyActionRow(g.Key, g.Sum(t => t.Points), g.Count()))
            .OrderByDescending(r => r.Points)
            .ToListAsync(cancellationToken);

        var active = await transactions.Select(t => t.UserId).Distinct().CountAsync(cancellationToken);

        var members = await context.Users
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Points = g.Sum(u => (long)u.LoyaltyPoints),
                Bonus = g.Sum(u => u.WalletBonusBalance),
                WithPoints = g.Count(u => u.LoyaltyPoints > 0),
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Value of a point = best Toman-per-point among the active wallet-credit rewards.
        var pointValue = await context.LoyaltyRewards
            .Where(r => r.IsActive && r.Type == LoyaltyRewardType.WalletCredit && r.PointsCost > 0)
            .Select(r => r.Value / r.PointsCost)
            .OrderByDescending(v => v)
            .FirstOrDefaultAsync(cancellationToken);

        var wallet = context.WalletTransactions.Where(w => w.BonusAmount > 0
                                                           && (w.Type == WalletTransactionType.LoyaltyReward || w.Type == WalletTransactionType.PromotionCredit));
        if (period.From.HasValue)
            wallet = wallet.Where(w => w.CreatedAt >= period.From.Value);
        if (period.To.HasValue)
            wallet = wallet.Where(w => w.CreatedAt < period.To.Value);
        var granted = await wallet.SumAsync(w => w.BonusAmount, cancellationToken);

        int Sum(LoyaltyTransactionType type) => byType.Where(t => t.Type == type).Sum(t => t.Points);

        return new LoyaltyReportDto
        {
            Period = period,
            PointsEarned = Sum(LoyaltyTransactionType.Earn),
            PointsRedeemed = -Sum(LoyaltyTransactionType.Redeem),
            PointsExpired = -Sum(LoyaltyTransactionType.Expire),
            PointsReverted = -Sum(LoyaltyTransactionType.Revert),
            PointsAdjusted = Sum(LoyaltyTransactionType.AdminAdjust),
            EarnedByAction = byAction,
            OutstandingPoints = members?.Points ?? 0,
            OutstandingBonusCredit = members?.Bonus ?? 0,
            MembersWithPoints = members?.WithPoints ?? 0,
            ActiveMembers = active,
            PointValue = Math.Round(pointValue, 2),
            BonusCreditGranted = granted,
        };
    }

    public async Task<ProfitReportDto> GetProfitReportAsync(ReportPeriod period, int topProducts = 10, CancellationToken cancellationToken = default)
    {
        var orders = RealizedOrders(period);

        var orderTotals = await orders
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Discount = g.Sum(o => o.DiscountAmount), Shipping = g.Sum(o => o.ShippingAmount) })
            .FirstOrDefaultAsync(cancellationToken);

        var items = context.OrderItems.Where(i => orders.Any(o => o.Id == i.OrderId));

        var itemTotals = await items
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Revenue = g.Sum(i => i.TotalAmount),
                CostedRevenue = g.Where(i => i.UnitCostPrice != null).Sum(i => i.TotalAmount),
                Cost = g.Where(i => i.UnitCostPrice != null).Sum(i => i.UnitCostPrice!.Value * i.Quantity),
                WithoutCost = g.Count(i => i.UnitCostPrice == null),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var revenue = itemTotals?.Revenue ?? 0;
        var costedRevenue = itemTotals?.CostedRevenue ?? 0;
        var cost = itemTotals?.Cost ?? 0;
        var orderDiscounts = orderTotals?.Discount ?? 0;

        // Order-level discounts are spread over the items by revenue share; only the costed share counts.
        var costedDiscount = revenue == 0 ? 0 : orderDiscounts * costedRevenue / revenue;
        var profit = costedRevenue - costedDiscount - cost;
        var costedNet = costedRevenue - costedDiscount;

        var top = await items
            .Where(i => i.UnitCostPrice != null)
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductName,
                Quantity = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.TotalAmount),
                Cost = g.Sum(i => i.UnitCostPrice!.Value * i.Quantity),
            })
            .OrderByDescending(r => r.Revenue - r.Cost)
            .Take(Math.Clamp(topProducts, 1, 100))
            .ToListAsync(cancellationToken);

        return new ProfitReportDto
        {
            Period = period,
            OrderCount = orderTotals?.Count ?? 0,
            ItemsRevenue = revenue,
            OrderDiscounts = orderDiscounts,
            Cost = cost,
            RevenueWithoutCost = revenue - costedRevenue,
            ItemsWithoutCost = itemTotals?.WithoutCost ?? 0,
            GrossProfit = Math.Round(profit, 0),
            MarginPercent = costedNet == 0 ? 0 : Math.Round(profit * 100 / costedNet, 1),
            ShippingCollected = orderTotals?.Shipping ?? 0,
            TopProducts = top.Select(r => new ProductProfitRow(r.ProductId, r.ProductName, r.Quantity, r.Revenue, r.Cost, r.Revenue - r.Cost)).ToList(),
        };
    }
}
