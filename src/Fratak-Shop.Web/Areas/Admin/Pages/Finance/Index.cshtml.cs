using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Reports;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Finance;

[AdminPermission(AdminPermissions.FinanceView)]
public class IndexModel(IAdminReportService adminReports, IReportService reports, Fratak_Shop.Core.Services.Insights.IExpenseService expenses) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "period")] public string? Preset { get; set; }
    [BindProperty(SupportsGet = true, Name = "from")] public string? From { get; set; }
    [BindProperty(SupportsGet = true, Name = "to")] public string? To { get; set; }

    public ResolvedPeriod Period { get; private set; } = null!;
    public SalesReportDto Sales { get; private set; } = new();
    public SalesReportDto? PreviousSales { get; private set; }
    public PurchaseReportDto Purchases { get; private set; } = new();
    public ProfitReportDto Profit { get; private set; } = new();
    public DiscountReportDto Discounts { get; private set; } = new();
    public LiabilitiesDto Liabilities { get; private set; } = new();
    public WalletReportDto Wallet { get; private set; } = new();
    public PaymentsReportDto Payments { get; private set; } = new();
    public LoyaltyReportDto Loyalty { get; private set; } = new();

    /// <summary>جمع هزینه‌های جاری بازه (صفحهٔ «هزینه‌ها»)؛ سود خالص = سود ناخالص واقعی − این مبلغ (فاز ۷ب).</summary>
    public decimal Expenses { get; private set; }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Period = AdminPeriods.Resolve(Preset, From, To);
        var p = Period.Current;
        Sales = await adminReports.GetSalesAsync(p, ct);
        if (Period.Previous is { } previous)
            PreviousSales = await adminReports.GetSalesAsync(previous, ct);
        Purchases = await adminReports.GetPurchasesAsync(p, ct);
        Profit = await reports.GetProfitReportAsync(p, 15, ct);
        Discounts = await reports.GetDiscountReportAsync(p, ct);
        Liabilities = await adminReports.GetLiabilitiesAsync(ct);
        Wallet = await adminReports.GetWalletAsync(p, ct);
        Payments = await adminReports.GetPaymentsReportAsync(p, ct);
        Loyalty = await reports.GetLoyaltyReportAsync(p, ct);
        Expenses = await expenses.GetTotalAsync(p, ct);
    }

    [AdminPermission(AdminPermissions.FinanceExport)]
    public async Task<IActionResult> OnGetExportAsync(string report = "summary", string format = "xlsx")
    {
        var ct = HttpContext.RequestAborted;
        Period = AdminPeriods.Resolve(Preset, From, To);
        var p = Period.Current;
        var suffix = Period.Preset == "custom" ? $"{Period.FromText}-{Period.ToText}".Replace("/", "") : Period.Preset;

        switch (report)
        {
            case "items":
            {
                var rows = await adminReports.GetSoldItemsAsync(p, ct);
                var table = new ExportTable("اقلام فروخته‌شده", "شماره سفارش", "تاریخ", "محصول", "SKU", "دسته", "برند", "تأمین‌کننده", "تعداد", "قیمت واحد", "تخفیف", "جمع", "بهای تمام‌شده واحد", "سود تخمینی");
                foreach (var r in rows)
                    table.Add(r.OrderNumber, r.CreatedAt, r.ProductName, r.SKU, r.Category, r.Brand, r.Supplier, r.Quantity, r.UnitPrice, r.Discount, r.Total, r.UnitCost,
                        r.UnitCost is { } cost ? r.Total - cost * r.Quantity : null);
                return Exporter.File(table, format, $"sold-items-{suffix}");
            }
            case "daily":
            {
                var sales = await adminReports.GetSalesAsync(p, ct);
                var table = new ExportTable("فروش روزانه", "روز / ماه", "فروش خالص (تومان)", "تعداد سفارش");
                foreach (var d in sales.Daily)
                    table.Add(d.Label, d.Value, (int)d.Secondary);
                return Exporter.File(table, format, $"daily-sales-{suffix}");
            }
            case "breakdown":
            {
                var sales = await adminReports.GetSalesAsync(p, ct);
                var table = new ExportTable("تفکیک فروش", "تفکیک", "عنوان", "تعداد", "مبلغ (تومان)");
                void AddAll(string group, IEnumerable<NamedAmount> items)
                {
                    foreach (var i in items)
                        table.Add(group, i.Name, i.Count, i.Amount);
                }
                AddAll("دسته‌بندی", sales.ByCategory);
                AddAll("برند", sales.TopBrands);
                AddAll("محصول", sales.TopProducts);
                AddAll("استان", sales.ByProvince);
                AddAll("روش ارسال", sales.ByShippingMethod);
                AddAll("روش پرداخت", sales.ByPaymentMethod);
                AddAll("مشتری", sales.TopCustomers);
                return Exporter.File(table, format, $"sales-breakdown-{suffix}");
            }
            case "coupons":
            {
                var discounts = await reports.GetDiscountReportAsync(p, ct);
                var table = new ExportTable("کوپن‌ها", "کد", "تعداد سفارش", "تخفیف (تومان)", "فروش (تومان)");
                foreach (var c in discounts.Coupons)
                    table.Add(c.Code, c.Orders, c.Discount, c.Sales);
                return Exporter.File(table, format, $"coupons-{suffix}");
            }
            case "profit":
            {
                var profit = await reports.GetProfitReportAsync(p, 500, ct);
                var table = new ExportTable("سود محصولات", "محصول", "تعداد", "درآمد", "بهای تمام‌شده", "سود");
                foreach (var r in profit.TopProducts)
                    table.Add(r.ProductName, r.Quantity, r.Revenue, r.Cost, r.Profit);
                return Exporter.File(table, format, $"product-profit-{suffix}");
            }
            default:
            {
                await OnGetAsync();
                var table = new ExportTable("خلاصه مالی", "شاخص", "مقدار");
                table.Add("بازه", Period.Label)
                    .Add("تعداد سفارش (قطعی)", Sales.OrderCount)
                    .Add("تعداد کالای فروخته‌شده", Sales.ItemsSold)
                    .Add("فروش ناخالص کالا", Sales.GrossSales)
                    .Add("تخفیف‌ها", Sales.Discounts)
                    .Add("هزینه ارسال دریافتی", Sales.Shipping)
                    .Add("فروش خالص", Sales.NetSales)
                    .Add("مبالغ بازگشتی", Sales.Refunds)
                    .Add("اعتبار هدیه مصرف‌شده", Sales.WalletBonusSpent)
                    .Add("میانگین سبد خرید", Sales.AverageOrderValue)
                    .Add("مشتریان خریدار", Sales.PayingCustomers)
                    .Add("مشتریان تکراری", Sales.RepeatCustomers)
                    .Add("مشتریان جدید", Sales.NewCustomers)
                    .Add("سفارش‌های لغوشده", Sales.CancelledCount)
                    .Add("مبلغ سفارش‌های لغوشده", Sales.CancelledAmount)
                    .Add("سفارش‌های مرجوعی", Sales.ReturnedCount)
                    .Add("جمع فاکتورهای خرید", Purchases.InvoiceTotal)
                    .Add("فروش سفارش‌های دارای فاکتور خرید", Purchases.SalesOfOrdersWithInvoice)
                    .Add("سود ناخالص واقعی (بر اساس فاکتور خرید)", Purchases.ActualGrossProfit)
                    .Add("حاشیه سود واقعی (%)", Purchases.ActualMarginPercent)
                    .Add("سفارش‌های بدون فاکتور خرید", Purchases.OrdersMissingInvoice)
                    .Add("سود ناخالص تخمینی (بر اساس قیمت خرید محصول)", Profit.GrossProfit)
                    .Add("حاشیه سود تخمینی (%)", Profit.MarginPercent)
                    .Add("تراکنش موفق درگاه", Payments.Succeeded)
                    .Add("نرخ موفقیت پرداخت (%)", Payments.SuccessRate)
                    .Add("مبلغ پرداخت‌های موفق", Payments.SucceededAmount)
                    .Add("برداشت‌های پرداخت‌شده", Wallet.WithdrawalsPaid)
                    .Add("کارت هدیه فروخته‌شده", Wallet.GiftCardsSold)
                    .Add("بدهی کیف پول مشتریان (فعلی)", Liabilities.WalletTotal)
                    .Add("  - قابل برداشت", Liabilities.WalletWithdrawable)
                    .Add("  - اعتبار هدیه", Liabilities.WalletBonus)
                    .Add("کارت‌های هدیه فعال", Liabilities.ActiveGiftCardsAmount)
                    .Add("ارزش امتیازهای باشگاه", Liabilities.OutstandingPointsValue)
                    .Add("جمع تعهدات", Liabilities.Total);
                return Exporter.File(table, format, $"finance-summary-{suffix}");
            }
        }
    }
}
