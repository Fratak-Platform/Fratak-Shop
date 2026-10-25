using System.Globalization;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Reports;
using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Payments;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>Dashboard and financial reports of the admin panel (sales, purchases & profit, liabilities, wallet, payments).</summary>
public interface IAdminReportService
{
    Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);

    Task<SalesReportDto> GetSalesAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    Task<PurchaseReportDto> GetPurchasesAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    Task<LiabilitiesDto> GetLiabilitiesAsync(CancellationToken cancellationToken = default);

    Task<WalletReportDto> GetWalletAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    Task<PaymentsReportDto> GetPaymentsReportAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    Task<PagedList<AdminPaymentRow>> GetPaymentsAsync(PaymentFilter filter);

    /// <summary>Order lines of realized orders (for the "sold items" export).</summary>
    Task<List<SoldItemRow>> GetSoldItemsAsync(ReportPeriod period, CancellationToken cancellationToken = default);
}

public sealed record SoldItemRow(string OrderNumber, DateTimeOffset CreatedAt, string ProductName, string SKU, string? Category, string? Brand,
    int Quantity, decimal UnitPrice, decimal Discount, decimal Total, decimal? UnitCost, string? Supplier);

public class AdminReportService(FratakShopDbContext context, IReportService reports, IStoreSettingService settings) : IAdminReportService
{
    private static readonly PersianCalendar Persian = new();

    /// <summary>Same rule as the other reports: not cancelled / returned, and paid or delivered (COD).</summary>
    private IQueryable<Order> Realized(ReportPeriod period)
    {
        var query = context.Orders.Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned
                                              && (o.PaymentStatus == PaymentStatus.Paid || o.Status == OrderStatus.Delivered));
        if (period.From.HasValue)
            query = query.Where(o => o.CreatedAt >= period.From.Value);
        if (period.To.HasValue)
            query = query.Where(o => o.CreatedAt < period.To.Value);
        return query;
    }

    private static string DayLabel(DateOnly day)
    {
        var date = day.ToDateTime(TimeOnly.MinValue);
        return $"{Persian.GetMonth(date):00}/{Persian.GetDayOfMonth(date):00}";
    }

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var today = IranClock.Today(now);
        var todayStart = IranClock.StartOfDay(today);
        var yesterdayStart = IranClock.StartOfDay(today.AddDays(-1));
        var start30 = IranClock.StartOfDay(today.AddDays(-29));
        var start60 = IranClock.StartOfDay(today.AddDays(-59));
        var lowStock = (await settings.GetAsync(cancellationToken)).LowStockThreshold;

        var recent = await Realized(new ReportPeriod(start60, null))
            .Select(o => new { o.CreatedAt, o.PayableAmount, o.RefundedAmount })
            .ToListAsync(cancellationToken);

        decimal Sum(DateTimeOffset from, DateTimeOffset? to) => recent.Where(o => o.CreatedAt >= from && (to == null || o.CreatedAt < to)).Sum(o => o.PayableAmount - o.RefundedAmount);
        int Count(DateTimeOffset from, DateTimeOffset? to) => recent.Count(o => o.CreatedAt >= from && (to == null || o.CreatedAt < to));

        var dto = new DashboardDto
        {
            TodaySales = Sum(todayStart, null),
            TodayOrders = await context.Orders.CountAsync(o => o.CreatedAt >= todayStart, cancellationToken),
            YesterdaySales = Sum(yesterdayStart, todayStart),
            YesterdayOrders = await context.Orders.CountAsync(o => o.CreatedAt >= yesterdayStart && o.CreatedAt < todayStart, cancellationToken),
            Last7Sales = Sum(IranClock.StartOfDay(today.AddDays(-6)), null),
            Last30Sales = Sum(start30, null),
            Last30Orders = Count(start30, null),
            Previous30Sales = Sum(start60, start30),
            TotalCustomers = await context.Users.CountAsync(cancellationToken),
            NewCustomersToday = await context.Users.CountAsync(u => u.CreatedAt >= todayStart, cancellationToken),
            NewCustomers30 = await context.Users.CountAsync(u => u.CreatedAt >= start30, cancellationToken),
            ActiveProducts = await context.Products.CountAsync(p => p.IsActive, cancellationToken),
            OutOfStock = await context.Products.CountAsync(p => p.IsActive && p.StockQuantity <= 0, cancellationToken),
            LowStock = await context.Products.CountAsync(p => p.IsActive && p.StockQuantity > 0 && p.StockQuantity <= lowStock, cancellationToken),

            OrdersToConfirm = await context.Orders.CountAsync(o => o.Status == OrderStatus.Pending
                                                                   && (o.PaymentStatus == PaymentStatus.Paid || o.PaymentMethod != PaymentMethod.OnlineGateway), cancellationToken),
            OrdersToPrepare = await context.Orders.CountAsync(o => o.Status == OrderStatus.Confirmed, cancellationToken),
            OrdersAwaitingInvoice = await context.Orders.CountAsync(o => (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Processing)
                                                                         && !context.PurchaseInvoices.Any(i => i.OrderId == o.Id), cancellationToken),
            OrdersToShip = await context.Orders.CountAsync(o => o.Status == OrderStatus.Processing, cancellationToken),
            PendingComments = await context.Comments.CountAsync(c => c.Status == CommentStatus.Pending, cancellationToken),
            OpenTickets = await context.SupportTickets.CountAsync(t => t.Status == TicketStatus.Open || t.Status == TicketStatus.CustomerReplied, cancellationToken),
            PendingReturns = await context.ReturnRequests.CountAsync(r => r.Status == ReturnStatus.Pending || r.Status == ReturnStatus.Received, cancellationToken),
            PendingWithdrawals = await context.WithdrawalRequests.CountAsync(w => w.Status == WithdrawalStatus.Pending || w.Status == WithdrawalStatus.Approved, cancellationToken),
            NewMessages = await context.Messages.CountAsync(m => m.Status == MessageStatus.New, cancellationToken),
            DraftsForReview = await context.BlogPosts.CountAsync(p => !p.IsPublished && p.SubmittedForReviewAt != null, cancellationToken),
            Unresolved404 = await context.SeoNotFoundLogs.CountAsync(l => !l.IsResolved, cancellationToken),
            UnresolvedErrors = await context.ErrorLogs.CountAsync(e => !e.IsResolved && e.CreatedAt >= now.AddDays(-7), cancellationToken),
        };

        for (var day = today.AddDays(-29); day <= today; day = day.AddDays(1))
        {
            var from = IranClock.StartOfDay(day);
            var to = IranClock.StartOfDay(day.AddDays(1));
            dto.Last30Days.Add(new ChartPoint(DayLabel(day), Sum(from, to), Count(from, to)));
        }

        dto.LatestOrders = await context.Orders.OrderByDescending(o => o.CreatedAt).Take(8)
            .Select(o => new AdminOrderRow
            {
                Id = o.Id, OrderNumber = o.OrderNumber, CreatedAt = o.CreatedAt, CustomerName = o.CustomerName, CustomerPhone = o.CustomerPhone,
                ShippingProvince = o.ShippingProvince, ShippingCity = o.ShippingCity, Status = o.Status, PaymentStatus = o.PaymentStatus,
                PaymentMethod = o.PaymentMethod, PayableAmount = o.PayableAmount, ItemCount = o.Items.Sum(i => i.Quantity),
                PurchaseInvoiceCount = context.PurchaseInvoices.Count(i => i.OrderId == o.Id),
            })
            .ToListAsync(cancellationToken);

        dto.TopProducts = await TopProductsAsync(new ReportPeriod(start30, null), 5, cancellationToken);

        var lowList = await context.Products.Where(p => p.IsActive && p.StockQuantity <= lowStock)
            .OrderBy(p => p.StockQuantity).ThenByDescending(p => p.SalesCount).Take(8)
            .Select(p => new { p.Id, p.Name, p.StockQuantity }).ToListAsync(cancellationToken);
        dto.LowStockProducts = lowList.Select(p => (p.Id, p.Name, p.StockQuantity)).ToList();

        var wanted = await context.ProductAlerts.Where(a => a.IsActive && a.Type == ProductAlertType.BackInStock)
            .GroupBy(a => new { a.ProductId, a.Product.Name })
            .Select(g => new { g.Key.ProductId, g.Key.Name, Count = g.Count() })
            .OrderByDescending(g => g.Count).Take(5).ToListAsync(cancellationToken);
        dto.MostWantedOutOfStock = wanted.Select(w => new NamedAmount(w.Name, w.Count, 0, w.ProductId)).ToList();

        return dto;
    }

    private async Task<List<NamedAmount>> TopProductsAsync(ReportPeriod period, int take, CancellationToken cancellationToken)
    {
        var orderIds = Realized(period).Select(o => o.Id);
        var rows = await context.OrderItems.Where(i => orderIds.Contains(i.OrderId))
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new { g.Key.ProductId, g.Key.ProductName, Quantity = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.TotalAmount) })
            .OrderByDescending(g => g.Revenue).Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(r => new NamedAmount(r.ProductName, r.Quantity, r.Revenue, r.ProductId)).ToList();
    }

    public async Task<SalesReportDto> GetSalesAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var orders = await Realized(period)
            .Select(o => new
            {
                o.Id, o.CreatedAt, o.UserId, o.CustomerName, o.SubtotalAmount, o.DiscountAmount, o.CouponDiscountAmount, o.ShippingAmount,
                o.PayableAmount, o.RefundedAmount, o.WalletBonusAmount, o.PaymentMethod, o.ShippingProvince, o.ShippingMethod,
            })
            .ToListAsync(cancellationToken);

        var dto = new SalesReportDto
        {
            OrderCount = orders.Count,
            GrossSales = orders.Sum(o => o.SubtotalAmount),
            Discounts = orders.Sum(o => o.DiscountAmount + o.CouponDiscountAmount),
            Shipping = orders.Sum(o => o.ShippingAmount),
            NetSales = orders.Sum(o => o.PayableAmount - o.RefundedAmount),
            Refunds = orders.Sum(o => o.RefundedAmount),
            WalletBonusSpent = orders.Sum(o => o.WalletBonusAmount),
            PayingCustomers = orders.Where(o => o.UserId != null).Select(o => o.UserId).Distinct().Count(),
            RepeatCustomers = orders.Where(o => o.UserId != null).GroupBy(o => o.UserId).Count(g => g.Count() > 1),
        };

        var all = context.Orders.AsQueryable();
        if (period.From.HasValue)
            all = all.Where(o => o.CreatedAt >= period.From.Value);
        if (period.To.HasValue)
            all = all.Where(o => o.CreatedAt < period.To.Value);
        dto.CancelledCount = await all.CountAsync(o => o.Status == OrderStatus.Cancelled, cancellationToken);
        dto.CancelledAmount = await all.Where(o => o.Status == OrderStatus.Cancelled).SumAsync(o => (decimal?)o.PayableAmount, cancellationToken) ?? 0;
        dto.ReturnedCount = await all.CountAsync(o => o.Status == OrderStatus.Returned, cancellationToken);
        dto.UnpaidCount = await all.CountAsync(o => o.Status == OrderStatus.Pending && o.PaymentStatus != PaymentStatus.Paid, cancellationToken);

        var users = context.Users.AsQueryable();
        if (period.From.HasValue)
            users = users.Where(u => u.CreatedAt >= period.From.Value);
        if (period.To.HasValue)
            users = users.Where(u => u.CreatedAt < period.To.Value);
        dto.NewCustomers = await users.CountAsync(cancellationToken);

        // Daily series (Iran days). Long periods are grouped by Persian month.
        if (orders.Count > 0 || period.From.HasValue)
        {
            var first = IranClock.Today(period.From ?? orders.Min(o => o.CreatedAt));
            var last = IranClock.Today(period.To?.AddSeconds(-1) ?? DateTimeOffset.UtcNow);
            var byDay = orders.GroupBy(o => IranClock.Today(o.CreatedAt))
                .ToDictionary(g => g.Key, g => (Amount: g.Sum(o => o.PayableAmount - o.RefundedAmount), Count: g.Count()));

            if (last.DayNumber - first.DayNumber <= 92)
            {
                for (var day = first; day <= last; day = day.AddDays(1))
                {
                    byDay.TryGetValue(day, out var value);
                    dto.Daily.Add(new ChartPoint(DayLabel(day), value.Amount, value.Count));
                }
            }
            else
            {
                foreach (var month in byDay.GroupBy(d =>
                         {
                             var date = d.Key.ToDateTime(TimeOnly.MinValue);
                             return (Persian.GetYear(date), Persian.GetMonth(date));
                         }).OrderBy(g => g.Key))
                {
                    dto.Daily.Add(new ChartPoint($"{month.Key.Item1}/{month.Key.Item2:00}", month.Sum(d => d.Value.Amount), month.Sum(d => d.Value.Count)));
                }
            }
        }

        var byHour = orders.GroupBy(o => IranClock.ToLocal(o.CreatedAt).Hour).ToDictionary(g => g.Key, g => g.Count());
        for (var hour = 0; hour < 24; hour++)
            dto.HourOfDay.Add(new ChartPoint(hour.ToString("00"), byHour.GetValueOrDefault(hour)));

        dto.ByPaymentMethod = orders.GroupBy(o => o.PaymentMethod)
            .Select(g => new NamedAmount(PaymentMethodTitle(g.Key), g.Count(), g.Sum(o => o.PayableAmount - o.RefundedAmount)))
            .OrderByDescending(r => r.Amount).ToList();
        dto.ByProvince = orders.GroupBy(o => o.ShippingProvince)
            .Select(g => new NamedAmount(g.Key, g.Count(), g.Sum(o => o.PayableAmount - o.RefundedAmount)))
            .OrderByDescending(r => r.Amount).Take(15).ToList();
        dto.ByShippingMethod = orders.GroupBy(o => o.ShippingMethod ?? "نامشخص")
            .Select(g => new NamedAmount(g.Key, g.Count(), g.Sum(o => o.ShippingAmount)))
            .OrderByDescending(r => r.Count).ToList();
        dto.TopCustomers = orders.Where(o => o.UserId != null).GroupBy(o => o.UserId)
            .Select(g => new NamedAmount(g.First().CustomerName, g.Count(), g.Sum(o => o.PayableAmount - o.RefundedAmount), g.Key))
            .OrderByDescending(r => r.Amount).Take(10).ToList();

        var orderIds = Realized(period).Select(o => o.Id);
        var items = context.OrderItems.Where(i => orderIds.Contains(i.OrderId));
        dto.ItemsSold = await items.SumAsync(i => (int?)i.Quantity, cancellationToken) ?? 0;

        var byCategory = await items.GroupBy(i => i.Product != null ? i.Product.Category.Name : null)
            .Select(g => new { Name = g.Key, Quantity = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.TotalAmount) })
            .OrderByDescending(g => g.Revenue).Take(15).ToListAsync(cancellationToken);
        dto.ByCategory = byCategory.Select(c => new NamedAmount(c.Name ?? "محصولات حذف‌شده", c.Quantity, c.Revenue)).ToList();

        var byBrand = await items.Where(i => i.Product != null && i.Product.Brand != null)
            .GroupBy(i => i.Product!.Brand!.Name)
            .Select(g => new { Name = g.Key, Quantity = g.Sum(i => i.Quantity), Revenue = g.Sum(i => i.TotalAmount) })
            .OrderByDescending(g => g.Revenue).Take(10).ToListAsync(cancellationToken);
        dto.TopBrands = byBrand.Select(b => new NamedAmount(b.Name, b.Quantity, b.Revenue)).ToList();

        dto.TopProducts = await TopProductsAsync(period, 15, cancellationToken);
        return dto;
    }

    public async Task<PurchaseReportDto> GetPurchasesAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var invoices = context.PurchaseInvoices.AsQueryable();
        if (period.From.HasValue)
            invoices = invoices.Where(i => i.CreatedAt >= period.From.Value);
        if (period.To.HasValue)
            invoices = invoices.Where(i => i.CreatedAt < period.To.Value);

        var invoiceRows = await invoices.Select(i => new { i.OrderId, i.Amount, i.CreatedAt, Supplier = i.SupplierName ?? "نامشخص", i.SupplierId }).ToListAsync(cancellationToken);

        var realized = Realized(period);
        var ordersWithInvoice = await realized.Where(o => context.PurchaseInvoices.Any(i => i.OrderId == o.Id))
            .Select(o => new { o.Id, Net = o.PayableAmount - o.RefundedAmount - o.ShippingAmount }).ToListAsync(cancellationToken);
        var ids = ordersWithInvoice.Select(o => o.Id).ToHashSet();
        var costOfThoseOrders = await context.PurchaseInvoices.Where(i => ids.Contains(i.OrderId)).SumAsync(i => (decimal?)i.Amount, cancellationToken) ?? 0;

        var missing = await realized.Where(o => !context.PurchaseInvoices.Any(i => i.OrderId == o.Id))
            .Select(o => o.PayableAmount).ToListAsync(cancellationToken);

        var dto = new PurchaseReportDto
        {
            InvoiceCount = invoiceRows.Count,
            InvoiceTotal = costOfThoseOrders,
            OrdersWithInvoice = ordersWithInvoice.Count,
            SalesOfOrdersWithInvoice = ordersWithInvoice.Sum(o => o.Net),
            OrdersMissingInvoice = missing.Count,
            SalesMissingInvoice = missing.Sum(),
            BySupplier = invoiceRows.GroupBy(i => i.Supplier)
                .Select(g => new NamedAmount(g.Key, g.Count(), g.Sum(i => i.Amount), g.First().SupplierId))
                .OrderByDescending(r => r.Amount).ToList(),
        };

        foreach (var day in invoiceRows.GroupBy(i => IranClock.Today(i.CreatedAt)).OrderBy(g => g.Key))
            dto.Daily.Add(new ChartPoint(DayLabel(day.Key), day.Sum(i => i.Amount), day.Count()));

        return dto;
    }

    public async Task<LiabilitiesDto> GetLiabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var loyalty = await reports.GetLoyaltyReportAsync(ReportPeriod.LastDays(1), cancellationToken);
        return new LiabilitiesDto
        {
            WalletTotal = await context.Users.SumAsync(u => (decimal?)u.WalletBalance, cancellationToken) ?? 0,
            WalletBonus = await context.Users.SumAsync(u => (decimal?)u.WalletBonusBalance, cancellationToken) ?? 0,
            WalletHolders = await context.Users.CountAsync(u => u.WalletBalance > 0, cancellationToken),
            PendingWithdrawals = await context.WithdrawalRequests.CountAsync(w => w.Status == WithdrawalStatus.Pending, cancellationToken),
            PendingWithdrawalsAmount = await context.WithdrawalRequests.Where(w => w.Status == WithdrawalStatus.Pending).SumAsync(w => (decimal?)w.Amount, cancellationToken) ?? 0,
            ApprovedWithdrawals = await context.WithdrawalRequests.CountAsync(w => w.Status == WithdrawalStatus.Approved, cancellationToken),
            ApprovedWithdrawalsAmount = await context.WithdrawalRequests.Where(w => w.Status == WithdrawalStatus.Approved).SumAsync(w => (decimal?)w.Amount, cancellationToken) ?? 0,
            ActiveGiftCards = await context.GiftCards.CountAsync(g => g.Status == GiftCardStatus.Active, cancellationToken),
            ActiveGiftCardsAmount = await context.GiftCards.Where(g => g.Status == GiftCardStatus.Active).SumAsync(g => (decimal?)g.Amount, cancellationToken) ?? 0,
            OutstandingPoints = loyalty.OutstandingPoints,
            OutstandingPointsValue = loyalty.OutstandingPointsValue,
        };
    }

    public async Task<WalletReportDto> GetWalletAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var transactions = context.WalletTransactions.AsQueryable();
        if (period.From.HasValue)
            transactions = transactions.Where(t => t.CreatedAt >= period.From.Value);
        if (period.To.HasValue)
            transactions = transactions.Where(t => t.CreatedAt < period.To.Value);

        var byType = await transactions.GroupBy(t => t.Type)
            .Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(t => t.Amount), Bonus = g.Sum(t => t.BonusAmount) })
            .ToListAsync(cancellationToken);

        var gifts = context.GiftCards.AsQueryable();
        if (period.From.HasValue)
            gifts = gifts.Where(g => g.ActivatedAt >= period.From.Value);
        if (period.To.HasValue)
            gifts = gifts.Where(g => g.ActivatedAt < period.To.Value);

        var withdrawals = context.WithdrawalRequests.Where(w => w.Status == WithdrawalStatus.Paid);
        if (period.From.HasValue)
            withdrawals = withdrawals.Where(w => w.ProcessedAt >= period.From.Value);
        if (period.To.HasValue)
            withdrawals = withdrawals.Where(w => w.ProcessedAt < period.To.Value);

        return new WalletReportDto
        {
            ByType = byType.Select(t => (t.Key, t.Count, t.Amount, t.Bonus)).OrderBy(t => t.Key).ToList(),
            TotalIn = byType.Where(t => t.Amount > 0).Sum(t => t.Amount),
            TotalOut = -byType.Where(t => t.Amount < 0).Sum(t => t.Amount),
            WithdrawalsPaid = await withdrawals.SumAsync(w => (decimal?)w.Amount, cancellationToken) ?? 0,
            WithdrawalsPaidCount = await withdrawals.CountAsync(cancellationToken),
            GiftCardsSold = await gifts.Where(g => g.Status != GiftCardStatus.PendingPayment && g.Status != GiftCardStatus.Cancelled).SumAsync(g => (decimal?)g.Amount, cancellationToken) ?? 0,
            GiftCardsSoldCount = await gifts.CountAsync(g => g.Status != GiftCardStatus.PendingPayment && g.Status != GiftCardStatus.Cancelled, cancellationToken),
            GiftCardsRedeemed = await context.GiftCards.Where(g => g.Status == GiftCardStatus.Redeemed
                                                                   && (period.From == null || g.RedeemedAt >= period.From)
                                                                   && (period.To == null || g.RedeemedAt < period.To))
                .SumAsync(g => (decimal?)g.Amount, cancellationToken) ?? 0,
        };
    }

    public async Task<PaymentsReportDto> GetPaymentsReportAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var payments = context.PaymentTransactions.AsQueryable();
        if (period.From.HasValue)
            payments = payments.Where(p => p.CreatedAt >= period.From.Value);
        if (period.To.HasValue)
            payments = payments.Where(p => p.CreatedAt < period.To.Value);

        var byStatus = await payments.GroupBy(p => p.Status).Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(p => p.Amount) }).ToListAsync(cancellationToken);
        var byPurpose = await payments.Where(p => p.Status == PaymentTransactionStatus.Succeeded)
            .GroupBy(p => p.Purpose).Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(p => p.Amount) }).ToListAsync(cancellationToken);
        var failures = await payments.Where(p => p.Status == PaymentTransactionStatus.Failed)
            .GroupBy(p => new { p.ResultCode, p.Message }).Select(g => new { g.Key.ResultCode, g.Key.Message, Count = g.Count() })
            .OrderByDescending(g => g.Count).Take(10).ToListAsync(cancellationToken);

        int CountOf(PaymentTransactionStatus status) => byStatus.Where(s => s.Key == status).Sum(s => s.Count);

        return new PaymentsReportDto
        {
            Total = byStatus.Sum(s => s.Count),
            Succeeded = CountOf(PaymentTransactionStatus.Succeeded),
            Failed = CountOf(PaymentTransactionStatus.Failed),
            Cancelled = CountOf(PaymentTransactionStatus.Cancelled),
            PendingOrStuck = CountOf(PaymentTransactionStatus.Pending) + CountOf(PaymentTransactionStatus.Verifying),
            SucceededAmount = byStatus.Where(s => s.Key == PaymentTransactionStatus.Succeeded).Sum(s => s.Amount),
            ByPurpose = byPurpose.Select(p => (p.Key, p.Count, p.Amount)).ToList(),
            FailureReasons = failures.Select(f => (f.ResultCode, f.Message, f.Count)).ToList(),
        };
    }

    public async Task<PagedList<AdminPaymentRow>> GetPaymentsAsync(PaymentFilter filter)
    {
        var query = context.PaymentTransactions.AsQueryable();
        if (filter.Status is { } status)
            query = query.Where(p => p.Status == status);
        if (filter.Purpose is { } purpose)
            query = query.Where(p => p.Purpose == purpose);
        if (filter.From is { } from)
            query = query.Where(p => p.CreatedAt >= from);
        if (filter.To is { } to)
            query = query.Where(p => p.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = SearchText.NormalizeDigits(filter.Search);
            query = query.Where(p => (p.RefId != null && p.RefId.Contains(term)) || (p.Authority != null && p.Authority.Contains(term))
                                     || (p.Order != null && p.Order.OrderNumber.Contains(term)) || (p.User != null && p.User.PhoneNumber.Contains(term)));
        }

        var page = Math.Max(1, filter.Page);
        var take = Math.Clamp(filter.Take, 10, 200);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(p => p.CreatedAt).Skip((page - 1) * take).Take(take)
            .Select(p => new AdminPaymentRow
            {
                Id = p.Id, CreatedAt = p.CreatedAt, Gateway = p.Gateway, Purpose = p.Purpose, Amount = p.Amount, Status = p.Status,
                RefId = p.RefId, CardPan = p.CardPan, Authority = p.Authority, ResultCode = p.ResultCode, Message = p.Message, VerifiedAt = p.VerifiedAt,
                UserId = p.UserId, UserName = p.User != null ? (p.User.FirstName + " " + p.User.LastName).Trim() + " (" + p.User.PhoneNumber + ")" : null,
                OrderId = p.OrderId, OrderNumber = p.Order != null ? p.Order.OrderNumber : null,
            })
            .ToListAsync();
        return new PagedList<AdminPaymentRow>(items, total, page, take);
    }

    public async Task<List<SoldItemRow>> GetSoldItemsAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var orderIds = Realized(period).Select(o => o.Id);
        var rows = await context.OrderItems.Where(i => orderIds.Contains(i.OrderId))
            .OrderByDescending(i => i.Order.CreatedAt)
            .Take(100_000)
            .Select(i => new
            {
                i.Order.OrderNumber, i.Order.CreatedAt, i.ProductName, i.SKU,
                Category = i.Product != null ? i.Product.Category.Name : null,
                Brand = i.Product != null && i.Product.Brand != null ? i.Product.Brand.Name : null,
                i.Quantity, i.UnitPrice, i.DiscountAmount, i.TotalAmount, i.UnitCostPrice,
                Supplier = i.Product != null && i.Product.Supplier != null ? i.Product.Supplier.Name : null,
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new SoldItemRow(r.OrderNumber, r.CreatedAt, r.ProductName, r.SKU, r.Category, r.Brand, r.Quantity,
            r.UnitPrice, r.DiscountAmount, r.TotalAmount, r.UnitCostPrice, r.Supplier)).ToList();
    }

    public static string PaymentMethodTitle(PaymentMethod method) => method switch
    {
        PaymentMethod.OnlineGateway => "درگاه آنلاین",
        PaymentMethod.CashOnDelivery => "پرداخت در محل",
        PaymentMethod.BankTransfer => "کارت به کارت",
        PaymentMethod.Wallet => "کیف پول",
        _ => "نامشخص",
    };
}
