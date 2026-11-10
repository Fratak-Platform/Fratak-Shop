using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Products;

// ═══════════════════════════════════════════════════════════════════════════
//  انبار و موجودی (مرحلهٔ هفتم – فاز ۵)
//   • دفتر گردش موجودی: چه کسی، کی، چرا و چقدر موجودی را عوض کرد (ردیف‌ها را DbContext و StockLedger می‌نویسند).
//   • پیش‌بینی اتمام موجودی: با سرعت فروش روزهای اخیر حساب می‌شود چند روز دیگر کالا تمام می‌شود.
//  صفحهٔ استفاده: /admin/products/inventory و کارت «گردش موجودی» در ویرایش محصول.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>سطح هشدار پیش‌بینی.</summary>
public enum StockForecastLevel
{
    /// <summary>فروش اخیر ندارد؛ پیش‌بینی ممکن نیست.</summary>
    NoSales = 0,

    /// <summary>بیشتر از «به‌زودی» کافی است.</summary>
    Ok = 1,

    /// <summary>کمتر از ۱۴ روز (پیش‌فرض) کافی است.</summary>
    Soon = 2,

    /// <summary>کمتر از ۵ روز (پیش‌فرض) کافی است.</summary>
    Critical = 3,

    /// <summary>تمام شده ولی تقاضا داشته (فروش از دست رفته).</summary>
    OutOfStock = 4,
}

/// <summary>نتیجهٔ پیش‌بینی یک محصول.</summary>
public sealed record StockForecast(decimal DailyRate, decimal? DaysLeft, StockForecastLevel Level, int SuggestedReorder)
{
    /// <summary>دور ۲۴: روش پیش‌بینی (mean، holt، croston یا simple برای میانگین سادهٔ قدیمی).</summary>
    public string Method { get; init; } = "simple";
}

/// <summary>یک ردیف جدول پیش‌بینی.</summary>
public sealed record StockForecastRow(
    Guid ProductId,
    string Name,
    string Sku,
    string? ImageName,
    bool IsActive,
    int Stock,
    int SoldInWindow,
    StockForecast Forecast,
    DateTimeOffset? EstimatedOutAt);

/// <summary>دور ۲۴ (پیشنهاد ۶۷): یک قلم پیش‌نویس سفارش خرید.</summary>
public sealed record PurchaseDraftLine(Guid ProductId, string Name, string Sku, int Stock, decimal DailyRate, decimal? DaysLeft, int Quantity, decimal? UnitCost, string Method);

/// <summary>دور ۲۴: پیش‌نویس سفارش خرید یک تأمین‌کننده (null = تأمین‌کنندهٔ نامشخص).</summary>
public sealed record PurchaseDraftGroup(Guid? SupplierId, string SupplierName, string? Phone, IReadOnlyList<PurchaseDraftLine> Lines)
{
    /// <summary>جمع بهای تمام‌شدهٔ اقلامی که قیمت خرید دارند.</summary>
    public decimal TotalCost => Lines.Where(l => l.UnitCost is not null).Sum(l => l.UnitCost!.Value * l.Quantity);
}

/// <summary>یک ردیف دفتر موجودی برای نمایش.</summary>
public sealed record StockMovementRow(
    Guid Id,
    Guid ProductId,
    string? ProductName,
    string? ProductSku,
    int Delta,
    int? BalanceAfter,
    StockMovementReason Reason,
    Guid? OrderId,
    string? Reference,
    string? UserName,
    string? Note,
    DateTimeOffset CreatedAt);

/// <summary>فیلترهای دفتر موجودی.</summary>
public sealed record StockMovementQuery(Guid? ProductId = null, StockMovementReason? Reason = null, string? Search = null,
    DateTimeOffset? From = null, DateTimeOffset? Until = null, int Page = 1, int Take = 40);

/// <summary>آمار بالای صفحهٔ انبار.</summary>
public sealed record InventorySummary(int ActiveProducts, int OutOfStock, int Critical, int Soon, long TotalUnits, decimal StockValueAtCost, int MissingCost);

/// <summary>
/// قانون‌های پیش‌بینی (بدون دیتابیس) تا تست واحد ساده باشد.
/// </summary>
public static class StockForecastRules
{
    /// <summary>کمتر از این تعداد روز = «بحرانی».</summary>
    public const int CriticalDays = 5;

    /// <summary>کمتر از این تعداد روز = «به‌زودی تمام می‌شود».</summary>
    public const int SoonDays = 14;

    /// <summary>پیشنهاد خرید برای پوشش این تعداد روز است.</summary>
    public const int CoverDays = 21;

    /// <summary>
    /// پیش‌بینی با فروش ساده‌ی میانگین روزانه.
    /// مثال: موجودی ۶، فروش ۳۰ روز اخیر ۴۵ عدد → روزانه ۱٫۵ → ۴ روز باقی‌مانده (بحرانی)؛ پیشنهاد خرید = ۲۱×۱٫۵−۶ = ۲۵٫۵ → ۲۶ (گرد رو به بالا).
    /// </summary>
    public static StockForecast Estimate(int stock, int soldInWindow, int windowDays)
    {
        windowDays = Math.Max(1, windowDays);
        stock = Math.Max(0, stock);
        if (soldInWindow <= 0)
            return new StockForecast(0, null, StockForecastLevel.NoSales, 0);

        return FromRate(stock, (decimal)soldInWindow / windowDays);
    }

    /// <summary>
    /// دور ۲۴ (پیشنهاد ۵۱): پیش‌بینی با یادگیری از سری فروش روزانه (<see cref="Learning.DemandForecaster"/>: Holt + فصل هفتگی یا Croston).
    /// <paramref name="daily"/> = فروش هر روز از قدیمی به جدید. بدون فروش ← NoSales.
    /// مثال: موجودی ۱۰ و فروش رو به رشد که مدل ۲ در روز پیش‌بینی می‌کند ← ۵ روز (به‌زودی)، پیشنهاد خرید ۳۲.
    /// </summary>
    public static StockForecast EstimateSeries(int stock, IReadOnlyList<int> daily)
    {
        var sold = daily.Sum();
        if (sold <= 0)
            return new StockForecast(0, null, StockForecastLevel.NoSales, 0);
        var learned = Learning.DemandForecaster.Forecast(daily);
        var rate = learned.DailyRate > 0 ? learned.DailyRate : (decimal)sold / Math.Max(1, daily.Count);
        return FromRate(Math.Max(0, stock), rate) with { Method = learned.DailyRate > 0 ? learned.Method : "simple" };
    }

    /// <summary>سطح هشدار و پیشنهاد خرید از سرعت فروش روزانه (مشترک بین روش ساده و یادگیرنده).</summary>
    public static StockForecast FromRate(int stock, decimal rate)
    {
        stock = Math.Max(0, stock);
        if (rate <= 0)
            return new StockForecast(0, null, StockForecastLevel.NoSales, 0);
        var reorder = (int)Math.Max(0, Math.Ceiling(rate * CoverDays - stock));
        if (stock == 0)
            return new StockForecast(decimal.Round(rate, 2), 0, StockForecastLevel.OutOfStock, reorder);

        var days = decimal.Round(stock / rate, 1);
        var level = days < CriticalDays ? StockForecastLevel.Critical : days < SoonDays ? StockForecastLevel.Soon : StockForecastLevel.Ok;
        return new StockForecast(decimal.Round(rate, 2), days, level, level == StockForecastLevel.Ok ? 0 : reorder);
    }

    /// <summary>عنوان فارسی دلیل تغییر موجودی (برای جدول و فیلتر).</summary>
    public static string ReasonTitle(StockMovementReason reason) => reason switch
    {
        StockMovementReason.Manual => "ویرایش دستی",
        StockMovementReason.Sale => "فروش",
        StockMovementReason.OrderCancelled => "لغو سفارش",
        StockMovementReason.Return => "مرجوعی",
        StockMovementReason.Supplier => "تأمین‌کننده",
        StockMovementReason.Import => "ورود از فایل",
        StockMovementReason.Initial => "موجودی اولیه",
        StockMovementReason.ItemUnavailable => "ناموجودی سفارش",
        StockMovementReason.OrderEdit => "ویرایش سفارش",
        _ => "سیستم",
    };
}

public interface IInventoryService
{
    /// <summary>دفتر موجودی با فیلتر و صفحه‌بندی.</summary>
    Task<(List<StockMovementRow> Items, int Total)> ListMovementsAsync(StockMovementQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// پیش‌بینی برای محصولاتی که در <paramref name="windowDays"/> روز اخیر فروش داشته‌اند (به همراه سطح هشدار).
    /// </summary>
    Task<List<StockForecastRow>> ForecastAsync(int windowDays, StockForecastLevel? level = null, string? search = null, CancellationToken cancellationToken = default);

    /// <summary>پیش‌بینی یک محصول (کارت ویرایش محصول).</summary>
    Task<StockForecast> ForecastProductAsync(Guid productId, int windowDays = 30, CancellationToken cancellationToken = default);

    /// <summary>آمار کلی انبار.</summary>
    Task<InventorySummary> SummaryAsync(int windowDays, CancellationToken cancellationToken = default);

    /// <summary>
    /// دور ۲۴ (پیشنهاد ۶۷): پیش‌نویس خودکار سفارش خرید، گروه‌بندی‌شده بر اساس تأمین‌کننده، از پیش‌بینی یادگیرندهٔ فروش.
    /// فقط کالاهای فعالی که زیر ۱۴ روز (یا تمام) می‌شوند؛ تعداد = پوشش ۲۱ روز فروش پیش‌بینی‌شده منهای موجودی. چیزی ثبت یا ارسال نمی‌شود.
    /// </summary>
    Task<List<PurchaseDraftGroup>> PurchaseDraftAsync(int windowDays, CancellationToken cancellationToken = default);
}

public sealed class InventoryService(FratakShopDbContext context) : IInventoryService
{
    public async Task<(List<StockMovementRow> Items, int Total)> ListMovementsAsync(StockMovementQuery query, CancellationToken cancellationToken = default)
    {
        var rows = context.StockMovements.AsQueryable();
        if (query.ProductId is { } productId)
            rows = rows.Where(m => m.ProductId == productId);
        if (query.Reason is { } reason)
            rows = rows.Where(m => m.Reason == reason);
        if (query.From is { } from)
            rows = rows.Where(m => m.CreatedAt >= from);
        if (query.Until is { } until)
            rows = rows.Where(m => m.CreatedAt < until);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = SearchText.NormalizeDigits(query.Search.Trim());
            rows = rows.Where(m => m.Product.Name.Contains(term) || m.Product.SKU.Contains(term) || (m.Reference != null && m.Reference.Contains(term)));
        }

        var total = await rows.CountAsync(cancellationToken);
        var take = Math.Clamp(query.Take, 1, 200);
        var page = Math.Max(1, query.Page);
        var items = await rows.OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * take).Take(take)
            .Select(m => new StockMovementRow(m.Id, m.ProductId, m.Product.Name, m.Product.SKU, m.Delta, m.BalanceAfter, m.Reason,
                m.OrderId, m.Reference, m.UserName, m.Note, m.CreatedAt))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<List<StockForecastRow>> ForecastAsync(int windowDays, StockForecastLevel? level = null, string? search = null, CancellationToken cancellationToken = default)
    {
        windowDays = Math.Clamp(windowDays, 7, 180);
        var series = await SeriesAsync(windowDays, null, cancellationToken);
        var sold = series.ToDictionary(x => x.Key, x => x.Value.Sum());
        if (sold.Count == 0)
            return [];

        var ids = sold.Keys.ToList();
        var products = context.Products.Where(p => ids.Contains(p.Id));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = SearchText.NormalizeDigits(search.Trim());
            products = products.Where(p => p.Name.Contains(term) || p.SKU.Contains(term));
        }

        var list = await products
            .Select(p => new
            {
                p.Id, p.Name, p.SKU, p.IsActive, p.StockQuantity,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var rows = list.Select(p =>
            {
                var forecast = StockForecastRules.EstimateSeries(p.StockQuantity, series[p.Id]);
                DateTimeOffset? outAt = forecast.DaysLeft is { } days and > 0 ? now.AddDays((double)days) : null;
                return new StockForecastRow(p.Id, p.Name, p.SKU, p.Image, p.IsActive, p.StockQuantity, sold[p.Id], forecast, outAt);
            })
            .Where(r => level is null || r.Forecast.Level == level)
            // تمام‌شده‌ها و بحرانی‌ها اول، بعد کمترین روز باقی‌مانده.
            .OrderByDescending(r => r.Forecast.Level == StockForecastLevel.OutOfStock)
            .ThenBy(r => r.Forecast.DaysLeft ?? decimal.MaxValue)
            .ThenByDescending(r => r.SoldInWindow)
            .ToList();
        return rows;
    }

    public async Task<StockForecast> ForecastProductAsync(Guid productId, int windowDays = 30, CancellationToken cancellationToken = default)
    {
        windowDays = Math.Clamp(windowDays, 7, 180);
        var series = await SeriesAsync(windowDays, productId, cancellationToken);
        var stock = await context.Products.IgnoreQueryFilters().Where(p => p.Id == productId).Select(p => p.StockQuantity).FirstOrDefaultAsync(cancellationToken);
        return series.TryGetValue(productId, out var daily)
            ? StockForecastRules.EstimateSeries(stock, daily)
            : new StockForecast(0, null, StockForecastLevel.NoSales, 0);
    }

    public async Task<InventorySummary> SummaryAsync(int windowDays, CancellationToken cancellationToken = default)
    {
        var active = context.Products.Where(p => p.IsActive);
        var activeCount = await active.CountAsync(cancellationToken);
        var outOfStock = await active.CountAsync(p => p.StockQuantity <= 0, cancellationToken);
        var units = await active.SumAsync(p => (long)p.StockQuantity, cancellationToken);
        var value = await active.Where(p => p.PurchasePrice != null).SumAsync(p => p.PurchasePrice!.Value * p.StockQuantity, cancellationToken);
        var missingCost = await active.CountAsync(p => p.PurchasePrice == null && p.StockQuantity > 0, cancellationToken);

        var forecast = await ForecastAsync(windowDays, cancellationToken: cancellationToken);
        return new InventorySummary(activeCount, outOfStock,
            forecast.Count(r => r.IsActive && r.Forecast.Level == StockForecastLevel.Critical),
            forecast.Count(r => r.IsActive && r.Forecast.Level == StockForecastLevel.Soon),
            units, value, missingCost);
    }

    /// <summary>
    /// دور ۲۴ (پیشنهاد ۶۷): پیش‌نویس سفارش خرید به تفکیک تأمین‌کننده از کالاهای فعالی که پیش‌بینی، خرید پیشنهاد می‌دهد.
    /// مثال: دو کالای «تأمین‌کنندهٔ الف» با ۵ و ۹ روز موجودی ← یک گروه با دو ردیف (کم‌روزترین اول) و جمع بهای خرید.
    /// </summary>
    public async Task<List<PurchaseDraftGroup>> PurchaseDraftAsync(int windowDays, CancellationToken cancellationToken = default)
    {
        var rows = (await ForecastAsync(windowDays, null, null, cancellationToken))
            .Where(r => r.IsActive && r.Forecast.SuggestedReorder > 0)
            .ToList();
        if (rows.Count == 0)
            return [];
        var ids = rows.Select(r => r.ProductId).ToList();
        var info = await context.Products.Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.SupplierId, p.PurchasePrice, SupplierName = p.Supplier != null ? p.Supplier.Name : null, SupplierPhone = p.Supplier != null ? p.Supplier.Phone : null })
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        return rows
            .GroupBy(r => info.TryGetValue(r.ProductId, out var i) ? i.SupplierId : null)
            .Select(g =>
            {
                var first = info.GetValueOrDefault(g.First().ProductId);
                var lines = g.OrderBy(r => r.Forecast.DaysLeft ?? 0)
                    .Select(r => new PurchaseDraftLine(r.ProductId, r.Name, r.Sku, r.Stock, r.Forecast.DailyRate, r.Forecast.DaysLeft, r.Forecast.SuggestedReorder,
                        info.TryGetValue(r.ProductId, out var i) ? i.PurchasePrice : null, r.Forecast.Method))
                    .ToList();
                return new PurchaseDraftGroup(g.Key, g.Key is null ? "تأمین‌کنندهٔ نامشخص" : first?.SupplierName ?? "تأمین‌کننده", g.Key is null ? null : first?.SupplierPhone, lines);
            })
            .OrderBy(g => g.SupplierId is null ? 1 : 0).ThenByDescending(g => g.Lines.Count)
            .ToList();
    }

    /// <summary>
    /// دور ۲۴: فروش روزانهٔ هر کالا (روز ایران، از قدیمی به جدید، طول = <paramref name="windowDays"/>) برای مدل پیش‌بینی.
    /// سفارش‌های لغو/مرجوع‌شده حساب نمی‌شوند. فقط ستون‌های لازم خوانده می‌شود و سقف ۲۰۰ هزار ردیف دارد (هاست ضعیف هم کند نمی‌شود).
    /// </summary>
    private async Task<Dictionary<Guid, int[]>> SeriesAsync(int windowDays, Guid? productId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-windowDays);
        var items = context.OrderItems.Where(i => i.ProductId != null
                                                  && i.Order.CreatedAt >= since
                                                  && i.Order.Status != OrderStatus.Cancelled
                                                  && i.Order.Status != OrderStatus.Returned);
        if (productId is { } id)
            items = items.Where(i => i.ProductId == id);
        var rows = await items.Select(i => new { ProductId = i.ProductId!.Value, i.Order.CreatedAt, i.Quantity })
            .Take(200_000)
            .ToListAsync(cancellationToken);
        var today = IranClock.Today(now);
        var result = new Dictionary<Guid, int[]>();
        foreach (var row in rows)
        {
            var age = today.DayNumber - IranClock.Today(row.CreatedAt).DayNumber;
            if (age < 0 || age >= windowDays)
                continue;
            if (!result.TryGetValue(row.ProductId, out var daily))
                result[row.ProductId] = daily = new int[windowDays];
            daily[windowDays - 1 - age] += Math.Max(0, row.Quantity);
        }
        return result;
    }
}
