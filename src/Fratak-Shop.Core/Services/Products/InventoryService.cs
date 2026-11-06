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
public sealed record StockForecast(decimal DailyRate, decimal? DaysLeft, StockForecastLevel Level, int SuggestedReorder);

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

        var rate = (decimal)soldInWindow / windowDays;
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
        var sold = await SoldAsync(windowDays, null, cancellationToken);
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
                var forecast = StockForecastRules.Estimate(p.StockQuantity, sold[p.Id], windowDays);
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
        var sold = await SoldAsync(windowDays, productId, cancellationToken);
        var stock = await context.Products.IgnoreQueryFilters().Where(p => p.Id == productId).Select(p => p.StockQuantity).FirstOrDefaultAsync(cancellationToken);
        return StockForecastRules.Estimate(stock, sold.GetValueOrDefault(productId), windowDays);
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
    /// تعداد فروخته‌شدهٔ هر محصول در بازه (سفارش‌های لغو/مرجوع‌شده حساب نمی‌شوند).
    /// </summary>
    private async Task<Dictionary<Guid, int>> SoldAsync(int windowDays, Guid? productId, CancellationToken cancellationToken)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-windowDays);
        var items = context.OrderItems.Where(i => i.ProductId != null
                                                  && i.Order.CreatedAt >= since
                                                  && i.Order.Status != OrderStatus.Cancelled
                                                  && i.Order.Status != OrderStatus.Returned);
        if (productId is { } id)
            items = items.Where(i => i.ProductId == id);

        var rows = await items.GroupBy(i => i.ProductId!.Value)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.ProductId, r => r.Quantity);
    }
}
