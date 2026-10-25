using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Payments;
using Fratak_Shop.Data.Entities.Wallets;

namespace Fratak_Shop.Core.DTOs.Admin;

public sealed record ChartPoint(string Label, decimal Value, decimal Secondary = 0);

public sealed record NamedAmount(string Name, int Count, decimal Amount, Guid? Id = null);

public sealed class SalesReportDto
{
    public int OrderCount { get; set; }
    public int ItemsSold { get; set; }
    public decimal GrossSales { get; set; }
    public decimal Discounts { get; set; }
    public decimal Shipping { get; set; }
    public decimal NetSales { get; set; }
    public decimal Refunds { get; set; }
    public decimal WalletBonusSpent { get; set; }
    public decimal AverageOrderValue => OrderCount == 0 ? 0 : Math.Round(NetSales / OrderCount, 0);
    public int PayingCustomers { get; set; }
    public int RepeatCustomers { get; set; }
    public int NewCustomers { get; set; }
    public int CancelledCount { get; set; }
    public decimal CancelledAmount { get; set; }
    public int ReturnedCount { get; set; }
    public int UnpaidCount { get; set; }
    public List<ChartPoint> Daily { get; set; } = [];
    public List<ChartPoint> HourOfDay { get; set; } = [];
    public List<NamedAmount> ByPaymentMethod { get; set; } = [];
    public List<NamedAmount> ByCategory { get; set; } = [];
    public List<NamedAmount> ByProvince { get; set; } = [];
    public List<NamedAmount> ByShippingMethod { get; set; } = [];
    public List<NamedAmount> TopProducts { get; set; } = [];
    public List<NamedAmount> TopCustomers { get; set; } = [];
    public List<NamedAmount> TopBrands { get; set; } = [];
}

public sealed class PurchaseReportDto
{
    public int InvoiceCount { get; set; }
    public decimal InvoiceTotal { get; set; }
    public int OrdersWithInvoice { get; set; }
    public decimal SalesOfOrdersWithInvoice { get; set; }
    public decimal ActualGrossProfit => SalesOfOrdersWithInvoice - InvoiceTotal;
    public decimal ActualMarginPercent => SalesOfOrdersWithInvoice == 0 ? 0 : Math.Round(ActualGrossProfit * 100 / SalesOfOrdersWithInvoice, 1);
    public int OrdersMissingInvoice { get; set; }
    public decimal SalesMissingInvoice { get; set; }
    public List<NamedAmount> BySupplier { get; set; } = [];
    public List<ChartPoint> Daily { get; set; } = [];
}

public sealed class LiabilitiesDto
{
    public decimal WalletTotal { get; set; }
    public decimal WalletBonus { get; set; }
    public decimal WalletWithdrawable => WalletTotal - WalletBonus;
    public int WalletHolders { get; set; }
    public int PendingWithdrawals { get; set; }
    public decimal PendingWithdrawalsAmount { get; set; }
    public int ApprovedWithdrawals { get; set; }
    public decimal ApprovedWithdrawalsAmount { get; set; }
    public int ActiveGiftCards { get; set; }
    public decimal ActiveGiftCardsAmount { get; set; }
    public long OutstandingPoints { get; set; }
    public decimal OutstandingPointsValue { get; set; }
    public decimal Total => WalletTotal + ActiveGiftCardsAmount + OutstandingPointsValue;
}

public sealed class WalletReportDto
{
    public List<(WalletTransactionType Type, int Count, decimal Amount, decimal Bonus)> ByType { get; set; } = [];
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    public decimal WithdrawalsPaid { get; set; }
    public int WithdrawalsPaidCount { get; set; }
    public decimal GiftCardsSold { get; set; }
    public int GiftCardsSoldCount { get; set; }
    public decimal GiftCardsRedeemed { get; set; }
}

public sealed class PaymentsReportDto
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Cancelled { get; set; }
    public int PendingOrStuck { get; set; }
    public decimal SucceededAmount { get; set; }
    public decimal SuccessRate => Total == 0 ? 0 : Math.Round(Succeeded * 100m / Total, 1);
    public List<(PaymentPurpose Purpose, int Count, decimal Amount)> ByPurpose { get; set; } = [];
    public List<(int? Code, string? Message, int Count)> FailureReasons { get; set; } = [];
}

public sealed class PaymentFilter
{
    public string? Search { get; set; }
    public PaymentTransactionStatus? Status { get; set; }
    public PaymentPurpose? Purpose { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int Page { get; set; } = 1;
    public int Take { get; set; } = 30;
}

public sealed class DashboardDto
{
    public decimal TodaySales { get; set; }
    public int TodayOrders { get; set; }
    public decimal YesterdaySales { get; set; }
    public int YesterdayOrders { get; set; }
    public decimal Last7Sales { get; set; }
    public decimal Last30Sales { get; set; }
    public int Last30Orders { get; set; }
    public decimal Previous30Sales { get; set; }
    public int TotalCustomers { get; set; }
    public int NewCustomersToday { get; set; }
    public int NewCustomers30 { get; set; }
    public int ActiveProducts { get; set; }
    public int OutOfStock { get; set; }
    public int LowStock { get; set; }

    // Pending work
    public int OrdersToConfirm { get; set; }
    public int OrdersToPrepare { get; set; }
    public int OrdersAwaitingInvoice { get; set; }
    public int OrdersToShip { get; set; }
    public int PendingComments { get; set; }
    public int OpenTickets { get; set; }
    public int PendingReturns { get; set; }
    public int PendingWithdrawals { get; set; }
    public int NewMessages { get; set; }
    public int DraftsForReview { get; set; }
    public int Unresolved404 { get; set; }
    public int UnresolvedErrors { get; set; }

    public List<ChartPoint> Last30Days { get; set; } = [];
    public List<AdminOrderRow> LatestOrders { get; set; } = [];
    public List<NamedAmount> TopProducts { get; set; } = [];
    public List<(Guid Id, string Name, int Stock)> LowStockProducts { get; set; } = [];
    public List<NamedAmount> MostWantedOutOfStock { get; set; } = [];
}
