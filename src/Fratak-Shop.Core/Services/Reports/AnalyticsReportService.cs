using System.Globalization;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Reports;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Analytics;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Payments;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Reports;

// ═══════════════════════════════════════════════════════════════════════════
//  گزارش‌های تحلیلی (مرحلهٔ هفتم – فاز ۷): قیف فروش، منبع ورودی و کمپین‌ها، نقشهٔ حرارتی روز × ساعت،
//  عملکرد محصولات و گزارش تقاضا.
//  داده: VisitorDay/AnalyticsCounter (ثبت داخلی بازدید) + سفارش‌ها + جست‌وجوها + «خبرم کن».
//  صفحه‌ها: /admin/reports ، /admin/reports/products ، /admin/reports/demand ، /admin/reports/campaigns.
//  تعریف «فروش قطعی» همان گزارش‌های مالی است: لغو/مرجوع نشده و (پرداخت‌شده یا تحویل‌شده در پرداخت در محل).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک پلهٔ قیف. RateFromPrevious = درصد نسبت به پلهٔ قبل؛ RateFromStart = درصد نسبت به همهٔ بازدیدکننده‌ها.</summary>
public sealed record FunnelStep(string Key, string Title, int Count, double? RateFromPrevious, double RateFromStart);

/// <summary>یک ردیف منبع/کمپین: بازدیدکننده (یکتا)، سفارش قطعی و مبلغ آن.</summary>
public sealed record TrafficRow(string Source, string Medium, string? Campaign, int Visitors, int Orders, decimal Revenue)
{
    public string SourceTitle => TrafficRules.SourceTitle(Source);
    public string MediumTitle => TrafficRules.MediumTitle(Medium);

    /// <summary>نرخ تبدیل بازدیدکننده به سفارش (٪). اگر بازدیدی ثبت نشده (مثلاً سفارش قدیمی) null.</summary>
    public double? Conversion => Visitors == 0 ? null : Math.Round(Orders * 100d / Visitors, 2);
}

/// <summary>گزارش قیف فروش و خلاصهٔ بازدید یک بازه.</summary>
public sealed class FunnelReport
{
    public List<FunnelStep> Steps { get; set; } = [];
    public int Visitors { get; set; }
    public int PageViews { get; set; }
    public int Members { get; set; }
    public List<ChartPoint> DailyVisitors { get; set; } = [];
    public List<NamedAmount> Devices { get; set; } = [];
    public List<NamedAmount> LandingPages { get; set; } = [];
    public List<TrafficRow> Sources { get; set; } = [];

    /// <summary>سفارش‌های بازه که منبعشان ثبت نشده (قبل از فعال شدن آمار) در قیف شمرده نمی‌شوند.</summary>
    public int UntrackedOrders { get; set; }

    public bool HasData => Visitors > 0;
}

/// <summary>
/// نقشهٔ حرارتی روز هفته × ساعت (به وقت ایران). ردیف ۰ = شنبه … ۶ = جمعه؛ ستون = ساعت ۰ تا ۲۳.
/// </summary>
public sealed class HeatmapReport
{
    public int[][] Visits { get; set; } = NewGrid();
    public int[][] Orders { get; set; } = NewGrid();
    public int MaxVisits => Visits.Max(r => r.Max());
    public int MaxOrders => Orders.Max(r => r.Max());

    public static readonly string[] DayNames = ["شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه"];

    public static int[][] NewGrid() => Enumerable.Range(0, 7).Select(_ => new int[24]).ToArray();

    /// <summary>ردیف روز هفتهٔ ایرانی. مثال: شنبه → ۰، جمعه → ۶.</summary>
    public static int Row(DayOfWeek day) => ((int)day + 1) % 7;

    /// <summary>پرفروش‌ترین ساعت‌ها (برای پیشنهاد زمان ارسال پیامک/اعلان). مثال: «پنجشنبه ساعت ۲۱».</summary>
    public IEnumerable<(int Day, int Hour, int Count)> Peaks(int[][] grid, int take = 3) =>
        grid.SelectMany((row, d) => row.Select((count, h) => (Day: d, Hour: h, Count: count)))
            .Where(c => c.Count > 0).OrderByDescending(c => c.Count).Take(take);
}

/// <summary>یک ردیف «عملکرد محصول» در بازه.</summary>
public sealed record ProductPerformanceRow(
    Guid ProductId, string Name, string Sku, bool IsActive, int Stock, decimal Price,
    int Views, int AddToCarts, int Orders, int Units, decimal Revenue, decimal? Profit, int ReturnedUnits)
{
    /// <summary>درصد بازدید → افزودن به سبد.</summary>
    public double? ViewToCart => Views == 0 ? null : Math.Round(AddToCarts * 100d / Views, 1);

    /// <summary>درصد بازدید → خرید (تعداد سفارش / بازدید).</summary>
    public double? Conversion => Views == 0 ? null : Math.Round(Orders * 100d / Views, 2);

    /// <summary>درصد مرجوعی از تعداد فروخته‌شده.</summary>
    public double? ReturnRate => Units == 0 ? null : Math.Round(ReturnedUnits * 100d / Units, 1);
}

/// <summary>فیلتر و مرتب‌سازی گزارش عملکرد محصولات.</summary>
/// <param name="Sort">revenue | units | views | conversion | carts | profit | returns</param>
/// <param name="Filter">all | nosales (بازدید دارد ولی فروش ندارد) | sold | out (ناموجود)</param>
public sealed record ProductPerformanceQuery(ReportPeriod Period, string Sort = "revenue", string Filter = "all", string? Search = null, int Page = 1, int Take = 50);

public sealed record ZeroResultSearch(string Query, int Searches, DateTimeOffset LastSearchedAt);

public sealed record TopSearch(string Query, int Searches, int LastResultCount);

public sealed record WantedProduct(Guid ProductId, string Name, int Requests, int Stock, bool IsActive);

/// <summary>محصول ناموجودی که در بازه دیده شده؛ LostRevenue تخمینی است (بازدید × نرخ تبدیل سایت × قیمت).</summary>
public sealed record LostDemandRow(Guid ProductId, string Name, int Views, decimal Price, decimal LostRevenue);

/// <summary>گزارش تقاضا: چه می‌خواهند که نداریم.</summary>
public sealed class DemandReport
{
    public List<ZeroResultSearch> ZeroResults { get; set; } = [];
    public List<TopSearch> TopSearches { get; set; } = [];
    public List<WantedProduct> BackInStockRequests { get; set; } = [];
    public List<LostDemandRow> OutOfStockViewed { get; set; } = [];
    public List<ProductPerformanceRow> ViewedNotBought { get; set; } = [];

    /// <summary>نرخ تبدیل کل سایت در بازه (سفارش محصول / بازدید محصول) که در تخمین فروش ازدست‌رفته استفاده شد.</summary>
    public double SiteConversion { get; set; }
}

public interface IAnalyticsReportService
{
    Task<FunnelReport> GetFunnelAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    Task<HeatmapReport> GetHeatmapAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    /// <param name="includeProfit">فقط برای دارندگان دسترسی «قیمت خرید» یا «گزارش مالی».</param>
    Task<PagedList<ProductPerformanceRow>> GetProductPerformanceAsync(ProductPerformanceQuery query, bool includeProfit, CancellationToken cancellationToken = default);

    Task<DemandReport> GetDemandAsync(ReportPeriod period, CancellationToken cancellationToken = default);

    /// <summary>منبع/کمپین‌ها با بازدیدکننده، سفارش و مبلغ (حداکثر ۲۰۰ ردیف).</summary>
    Task<List<TrafficRow>> GetCampaignsAsync(ReportPeriod period, bool groupByCampaign, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class AnalyticsReportService(FratakShopDbContext context) : IAnalyticsReportService
{
    private static readonly PersianCalendar Persian = new();

    // ── بازه ────────────────────────────────────────────────────────────

    /// <summary>
    /// تبدیل بازهٔ زمانی گزارش به بازهٔ روزهای ایران [از، تا) برای جدول‌های روزانه.
    /// مثال: «۳۰ روز اخیر» → (امروز − ۲۹، فردا).
    /// </summary>
    public static (DateOnly From, DateOnly To) Days(ReportPeriod period)
    {
        var from = period.From is { } f ? IranClock.Today(f) : DateOnly.MinValue;
        var to = period.To is { } t ? IranClock.Today(t) : IranClock.Today(DateTimeOffset.UtcNow).AddDays(1);
        return (from, to);
    }

    private IQueryable<VisitorDay> VisitorsIn(ReportPeriod period)
    {
        var (from, to) = Days(period);
        return context.VisitorDays.AsNoTracking().Where(v => v.Day >= from && v.Day < to);
    }

    private IQueryable<Order> OrdersIn(ReportPeriod period)
    {
        var query = context.Orders.AsNoTracking().AsQueryable();
        if (period.From is { } from)
            query = query.Where(o => o.CreatedAt >= from);
        if (period.To is { } to)
            query = query.Where(o => o.CreatedAt < to);
        return query;
    }

    /// <summary>فروش قطعی: لغو/مرجوع نشده و پرداخت‌شده یا تحویل‌شده (پرداخت در محل).</summary>
    private static IQueryable<Order> Realized(IQueryable<Order> orders) =>
        orders.Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned
                          && (o.PaymentStatus == PaymentStatus.Paid || o.Status == OrderStatus.Delivered));

    // ── قیف ─────────────────────────────────────────────────────────────

    public async Task<FunnelReport> GetFunnelAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var visitors = VisitorsIn(period);
        var report = new FunnelReport
        {
            Visitors = await visitors.Select(v => v.VisitorKey).Distinct().CountAsync(cancellationToken),
            PageViews = await visitors.SumAsync(v => (int?)v.PageViews, cancellationToken) ?? 0,
            Members = await visitors.Where(v => v.UserId != null).Select(v => v.UserId).Distinct().CountAsync(cancellationToken),
        };

        var productViews = await CountStageAsync(visitors, FunnelStage.ProductView, cancellationToken);
        var carts = await CountStageAsync(visitors, FunnelStage.AddToCart, cancellationToken);
        var checkouts = await CountStageAsync(visitors, FunnelStage.Checkout, cancellationToken);

        // سفارش و پرداخت از جدول سفارش‌ها؛ فقط سفارش‌هایی که به یک بازدیدکنندهٔ ثبت‌شده وصل‌اند.
        var orders = OrdersIn(period).Where(o => o.VisitorKey != null);
        var placed = await orders.Select(o => o.VisitorKey).Distinct().CountAsync(cancellationToken);
        var paid = await Realized(orders).Select(o => o.VisitorKey).Distinct().CountAsync(cancellationToken);
        report.UntrackedOrders = await OrdersIn(period).CountAsync(o => o.VisitorKey == null, cancellationToken);

        report.Steps = BuildSteps(report.Visitors, productViews, carts, checkouts, placed, paid);

        var daily = await visitors.GroupBy(v => v.Day)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        report.DailyVisitors = FillDays(period, daily.ToDictionary(d => d.Day, d => d.Count));

        report.Devices = (await visitors.GroupBy(v => v.Device).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken))
            .OrderByDescending(d => d.Count)
            .Select(d => new NamedAmount(TrafficRules.DeviceTitle(d.Key), d.Count, d.Count))
            .ToList();

        report.LandingPages = (await visitors.Where(v => v.LandingPath != null)
                .GroupBy(v => v.LandingPath!).Select(g => new { g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count).Take(10).ToListAsync(cancellationToken))
            .Select(p => new NamedAmount(p.Key, p.Count, p.Count))
            .ToList();

        report.Sources = (await GetCampaignsAsync(period, groupByCampaign: false, cancellationToken)).Take(10).ToList();
        return report;
    }

    private static Task<int> CountStageAsync(IQueryable<VisitorDay> visitors, FunnelStage stage, CancellationToken cancellationToken)
    {
        var bit = (int)stage;
        return visitors.Where(v => (v.Stages & bit) != 0).Select(v => v.VisitorKey).Distinct().CountAsync(cancellationToken);
    }

    /// <summary>
    /// ساخت پله‌های قیف با درصدها. مثال: ۱۰۰۰ بازدید، ۴۰۰ دیدن محصول → پلهٔ دوم ۴۰٪ از قبل و ۴۰٪ از شروع.
    /// درصد از قبل حداکثر ۱۰۰ نمایش داده می‌شود (ممکن است کسی بدون دیدن صفحهٔ محصول، از فهرست به سبد افزوده باشد).
    /// </summary>
    public static List<FunnelStep> BuildSteps(int visitors, int productViews, int carts, int checkouts, int orders, int paid)
    {
        (string Key, string Title, int Count)[] raw =
        [
            ("visit", "بازدید از سایت", visitors),
            ("product", "دیدن صفحهٔ محصول", productViews),
            ("cart", "افزودن به سبد خرید", carts),
            ("checkout", "ورود به تسویه حساب", checkouts),
            ("order", "ثبت سفارش", orders),
            ("paid", "خرید قطعی", paid),
        ];
        var steps = new List<FunnelStep>(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            double? fromPrevious = i == 0 ? null : raw[i - 1].Count == 0 ? 0 : Math.Min(100, Math.Round(raw[i].Count * 100d / raw[i - 1].Count, 1));
            var fromStart = visitors == 0 ? 0 : Math.Min(100, Math.Round(raw[i].Count * 100d / visitors, 2));
            steps.Add(new FunnelStep(raw[i].Key, raw[i].Title, raw[i].Count, fromPrevious, fromStart));
        }
        return steps;
    }

    /// <summary>نمودار روزانه با روزهای بدون بازدید (صفر). برای بازه‌های بیش از ۱۲۰ روز فقط روزهای دارای داده.</summary>
    private static List<ChartPoint> FillDays(ReportPeriod period, Dictionary<DateOnly, int> counts)
    {
        if (counts.Count == 0)
            return [];
        var (from, to) = Days(period);
        var start = from == DateOnly.MinValue ? counts.Keys.Min() : from;
        var end = to.AddDays(-1);
        if (end.DayNumber - start.DayNumber > 120)
            return counts.OrderBy(c => c.Key).Select(c => new ChartPoint(DayLabel(c.Key), c.Value)).ToList();
        var points = new List<ChartPoint>();
        for (var day = start; day <= end; day = day.AddDays(1))
            points.Add(new ChartPoint(DayLabel(day), counts.GetValueOrDefault(day)));
        return points;
    }

    private static string DayLabel(DateOnly day)
    {
        var date = day.ToDateTime(TimeOnly.MinValue);
        return $"{Persian.GetMonth(date):00}/{Persian.GetDayOfMonth(date):00}";
    }

    // ── نقشهٔ حرارتی ────────────────────────────────────────────────────

    public async Task<HeatmapReport> GetHeatmapAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var report = new HeatmapReport();

        var visits = await VisitorsIn(period).GroupBy(v => new { v.Day, v.FirstHour })
            .Select(g => new { g.Key.Day, g.Key.FirstHour, Count = g.Count() })
            .ToListAsync(cancellationToken);
        foreach (var cell in visits)
            report.Visits[HeatmapReport.Row(cell.Day.DayOfWeek)][Math.Clamp((int)cell.FirstHour, 0, 23)] += cell.Count;

        // زمان سفارش باید به وقت ایران تبدیل شود که در SQL ممکن نیست؛ حداکثر ۵۰ هزار سفارش آخر بازه خوانده می‌شود.
        var times = await OrdersIn(period).Where(o => o.Status != OrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAt).Select(o => o.CreatedAt).Take(50_000)
            .ToListAsync(cancellationToken);
        foreach (var time in times)
        {
            var local = IranClock.ToLocal(time);
            report.Orders[HeatmapReport.Row(local.DayOfWeek)][local.Hour]++;
        }
        return report;
    }

    // ── عملکرد محصولات ─────────────────────────────────────────────────

    public async Task<PagedList<ProductPerformanceRow>> GetProductPerformanceAsync(ProductPerformanceQuery query, bool includeProfit, CancellationToken cancellationToken = default)
    {
        var rows = await BuildProductRowsAsync(query.Period, includeProfit, cancellationToken);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = SearchText.NormalizeDigits(query.Search.Trim());
            rows = rows.Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || r.Sku.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        rows = query.Filter switch
        {
            "nosales" => rows.Where(r => r.Views > 0 && r.Units == 0).ToList(),
            "sold" => rows.Where(r => r.Units > 0).ToList(),
            "out" => rows.Where(r => r.Stock <= 0).ToList(),
            _ => rows.Where(r => r.Views > 0 || r.Units > 0 || r.AddToCarts > 0).ToList(),
        };

        IEnumerable<ProductPerformanceRow> sorted = query.Sort switch
        {
            "units" => rows.OrderByDescending(r => r.Units).ThenByDescending(r => r.Revenue),
            "views" => rows.OrderByDescending(r => r.Views),
            "carts" => rows.OrderByDescending(r => r.AddToCarts),
            "conversion" => rows.Where(r => r.Views >= 10).OrderByDescending(r => r.Conversion).ThenByDescending(r => r.Views)
                .Concat(rows.Where(r => r.Views < 10).OrderByDescending(r => r.Views)),
            "profit" when includeProfit => rows.OrderByDescending(r => r.Profit ?? decimal.MinValue),
            "returns" => rows.OrderByDescending(r => r.ReturnRate ?? -1).ThenByDescending(r => r.ReturnedUnits),
            _ => rows.OrderByDescending(r => r.Revenue).ThenByDescending(r => r.Views),
        };

        var take = Math.Clamp(query.Take, 10, 200);
        var page = Math.Max(1, query.Page);
        var list = sorted.ToList();
        return new PagedList<ProductPerformanceRow>(list.Skip((page - 1) * take).Take(take).ToList(), list.Count, page, take);
    }

    /// <summary>
    /// همهٔ ستون‌های عملکرد برای همهٔ محصولات (بازدید و سبد از شمارنده‌ها؛ فروش و سود از اقلام سفارش‌های قطعی؛
    /// مرجوعی از درخواست‌های مرجوعی تأییدشده). برای فروشگاه‌های تا چند ده هزار محصول در حافظه سریع است.
    /// </summary>
    private async Task<List<ProductPerformanceRow>> BuildProductRowsAsync(ReportPeriod period, bool includeProfit, CancellationToken cancellationToken)
    {
        var (from, to) = Days(period);
        var counters = await context.AnalyticsCounters.AsNoTracking()
            .Where(c => c.Day >= from && c.Day < to && (c.Metric == AnalyticsMetrics.ProductView || c.Metric == AnalyticsMetrics.AddToCart))
            .GroupBy(c => new { c.Metric, c.Key })
            .Select(g => new { g.Key.Metric, g.Key.Key, Count = g.Sum(c => c.Count) })
            .ToListAsync(cancellationToken);
        var views = counters.Where(c => c.Metric == AnalyticsMetrics.ProductView).ToDictionary(c => c.Key, c => c.Count);
        var carts = counters.Where(c => c.Metric == AnalyticsMetrics.AddToCart).ToDictionary(c => c.Key, c => c.Count);

        var orderIds = Realized(OrdersIn(period)).Select(o => o.Id);
        var sales = await context.OrderItems.AsNoTracking()
            .Where(i => i.ProductId != null && orderIds.Contains(i.OrderId))
            .GroupBy(i => i.ProductId!.Value)
            .Select(g => new
            {
                ProductId = g.Key,
                Orders = g.Select(i => i.OrderId).Distinct().Count(),
                Units = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.TotalAmount),
                Cost = g.Sum(i => i.UnitCostPrice == null ? (decimal?)null : i.UnitCostPrice * i.Quantity),
                CostedUnits = g.Sum(i => i.UnitCostPrice == null ? 0 : i.Quantity),
            })
            .ToDictionaryAsync(s => s.ProductId, cancellationToken);

        var returnStatuses = new[] { ReturnStatus.Approved, ReturnStatus.Received, ReturnStatus.Refunded };
        var returns = await context.ReturnRequestItems.AsNoTracking()
            .Where(r => returnStatuses.Contains(r.ReturnRequest.Status) && r.OrderItem.ProductId != null && orderIds.Contains(r.OrderItem.OrderId))
            .GroupBy(r => r.OrderItem.ProductId!.Value)
            .Select(g => new { ProductId = g.Key, Units = g.Sum(r => r.Quantity) })
            .ToDictionaryAsync(r => r.ProductId, r => r.Units, cancellationToken);

        var products = await context.Products.AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.SKU, p.IsActive, p.StockQuantity, p.Price, p.DiscountPrice })
            .ToListAsync(cancellationToken);

        return products.Select(p =>
        {
            var key = p.Id.ToString("N");
            sales.TryGetValue(p.Id, out var sale);
            decimal? profit = null;
            // سود فقط وقتی معنا دارد که قیمت خرید همهٔ اقلام فروخته‌شده ثبت شده باشد.
            if (includeProfit && sale is not null && sale.CostedUnits == sale.Units && sale.Cost is { } cost)
                profit = sale.Revenue - cost;
            // قیمت فعلی تقریبی: تخفیف اگر کمتر از قیمت اصلی است (زمان‌بندی حراج در این گزارش بررسی نمی‌شود).
            var price = p.DiscountPrice is { } discount && discount > 0 && discount < p.Price ? discount : p.Price;
            return new ProductPerformanceRow(p.Id, p.Name, p.SKU, p.IsActive, p.StockQuantity, price,
                views.GetValueOrDefault(key), carts.GetValueOrDefault(key), sale?.Orders ?? 0, sale?.Units ?? 0, sale?.Revenue ?? 0, profit,
                returns.GetValueOrDefault(p.Id));
        }).ToList();
    }

    // ── تقاضا ───────────────────────────────────────────────────────────

    public async Task<DemandReport> GetDemandAsync(ReportPeriod period, CancellationToken cancellationToken = default)
    {
        var since = period.From ?? DateTimeOffset.MinValue;
        var report = new DemandReport
        {
            ZeroResults = (await context.SearchQueryLogs.AsNoTracking()
                    .Where(s => s.LastResultCount == 0 && !s.IsHidden && s.LastSearchedAt >= since)
                    .OrderByDescending(s => s.SearchCount).Take(30)
                    .Select(s => new { s.Query, s.SearchCount, s.LastSearchedAt })
                    .ToListAsync(cancellationToken))
                .Select(s => new ZeroResultSearch(s.Query, s.SearchCount, s.LastSearchedAt)).ToList(),
            TopSearches = (await context.SearchQueryLogs.AsNoTracking()
                    .Where(s => !s.IsHidden && s.LastSearchedAt >= since)
                    .OrderByDescending(s => s.SearchCount).Take(20)
                    .Select(s => new { s.Query, s.SearchCount, s.LastResultCount })
                    .ToListAsync(cancellationToken))
                .Select(s => new TopSearch(s.Query, s.SearchCount, s.LastResultCount)).ToList(),
            BackInStockRequests = (await context.ProductAlerts.AsNoTracking()
                    .Where(a => a.IsActive && a.Type == ProductAlertType.BackInStock && a.NotifiedAt == null)
                    .GroupBy(a => new { a.ProductId, a.Product.Name, a.Product.StockQuantity, a.Product.IsActive })
                    .Select(g => new { g.Key.ProductId, g.Key.Name, g.Key.StockQuantity, g.Key.IsActive, Count = g.Count() })
                    .OrderByDescending(g => g.Count).Take(20)
                    .ToListAsync(cancellationToken))
                .Select(a => new WantedProduct(a.ProductId, a.Name, a.Count, a.StockQuantity, a.IsActive)).ToList(),
        };

        var rows = await BuildProductRowsAsync(period, includeProfit: false, cancellationToken);
        var totalViews = rows.Sum(r => (long)r.Views);
        var totalOrders = rows.Sum(r => (long)r.Orders);
        // نرخ تبدیل سایت بین ۰٫۲٪ تا ۱۰٪ محدود می‌شود تا با دادهٔ کم، تخمین غیرواقعی نشود.
        report.SiteConversion = totalViews == 0 ? 0 : Math.Clamp(totalOrders / (double)totalViews, 0.002, 0.10);

        report.OutOfStockViewed = rows.Where(r => r.Stock <= 0 && r.Views > 0)
            .Select(r => new LostDemandRow(r.ProductId, r.Name, r.Views, r.Price, Math.Round(r.Views * (decimal)report.SiteConversion * r.Price / 1000m) * 1000m))
            .OrderByDescending(r => r.LostRevenue).ThenByDescending(r => r.Views).Take(20).ToList();
        report.ViewedNotBought = rows.Where(r => r.Stock > 0 && r.Views >= 20 && r.Units == 0)
            .OrderByDescending(r => r.Views).Take(20).ToList();
        return report;
    }

    // ── منبع ورودی و کمپین‌ها ──────────────────────────────────────────

    public async Task<List<TrafficRow>> GetCampaignsAsync(ReportPeriod period, bool groupByCampaign, CancellationToken cancellationToken = default)
    {
        var visitors = VisitorsIn(period);
        var orders = Realized(OrdersIn(period));

        List<(string Source, string Medium, string? Campaign, int Visitors)> visits;
        List<(string Source, string Medium, string? Campaign, int Orders, decimal Revenue)> sales;
        if (groupByCampaign)
        {
            visits = (await visitors.GroupBy(v => new { v.Source, v.Medium, v.Campaign })
                    .Select(g => new { g.Key.Source, g.Key.Medium, g.Key.Campaign, Count = g.Select(v => v.VisitorKey).Distinct().Count() })
                    .ToListAsync(cancellationToken))
                .Select(v => (v.Source, v.Medium, v.Campaign, v.Count)).ToList();
            sales = (await orders.GroupBy(o => new { o.TrafficSource, o.TrafficMedium, o.TrafficCampaign })
                    .Select(g => new { g.Key.TrafficSource, g.Key.TrafficMedium, g.Key.TrafficCampaign, Count = g.Count(), Revenue = g.Sum(o => o.PayableAmount - o.RefundedAmount) })
                    .ToListAsync(cancellationToken))
                .Select(o => (o.TrafficSource ?? TrafficRules.DirectSource, o.TrafficMedium ?? TrafficRules.DirectSource, o.TrafficCampaign, o.Count, o.Revenue)).ToList();
        }
        else
        {
            visits = (await visitors.GroupBy(v => new { v.Source, v.Medium })
                    .Select(g => new { g.Key.Source, g.Key.Medium, Count = g.Select(v => v.VisitorKey).Distinct().Count() })
                    .ToListAsync(cancellationToken))
                .Select(v => (v.Source, v.Medium, (string?)null, v.Count)).ToList();
            sales = (await orders.GroupBy(o => new { o.TrafficSource, o.TrafficMedium })
                    .Select(g => new { g.Key.TrafficSource, g.Key.TrafficMedium, Count = g.Count(), Revenue = g.Sum(o => o.PayableAmount - o.RefundedAmount) })
                    .ToListAsync(cancellationToken))
                .Select(o => (o.TrafficSource ?? TrafficRules.DirectSource, o.TrafficMedium ?? TrafficRules.DirectSource, (string?)null, o.Count, o.Revenue)).ToList();
        }

        return MergeTraffic(visits, sales).Take(200).ToList();
    }

    /// <summary>
    /// ترکیب بازدید و فروش هر منبع با کلید (منبع، نوع، کمپین). مرتب‌سازی: مبلغ فروش، بعد بازدیدکننده.
    /// </summary>
    public static IEnumerable<TrafficRow> MergeTraffic(
        IEnumerable<(string Source, string Medium, string? Campaign, int Visitors)> visits,
        IEnumerable<(string Source, string Medium, string? Campaign, int Orders, decimal Revenue)> sales)
    {
        var map = new Dictionary<(string, string, string), TrafficRow>();
        foreach (var v in visits)
        {
            var key = (v.Source, v.Medium, v.Campaign ?? "");
            map[key] = map.TryGetValue(key, out var row)
                ? row with { Visitors = row.Visitors + v.Visitors }
                : new TrafficRow(v.Source, v.Medium, v.Campaign, v.Visitors, 0, 0);
        }
        foreach (var s in sales)
        {
            var key = (s.Source, s.Medium, s.Campaign ?? "");
            map[key] = map.TryGetValue(key, out var row)
                ? row with { Orders = row.Orders + s.Orders, Revenue = row.Revenue + s.Revenue }
                : new TrafficRow(s.Source, s.Medium, s.Campaign, 0, s.Orders, s.Revenue);
        }
        return map.Values.OrderByDescending(r => r.Revenue).ThenByDescending(r => r.Visitors).ThenBy(r => r.Source, StringComparer.Ordinal);
    }
}
