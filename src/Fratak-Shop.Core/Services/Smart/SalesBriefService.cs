using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// محافظ سود (C27): قیمت فروش یا تخفیفی که کمتر از قیمت خرید است ذخیره نمی‌شود.
/// <para>مثال: قیمت خرید ۸۰۰ هزار، قیمت ۱ میلیون و تخفیف ۷۵۰ هزار ← خطا «زیان: …». قیمت ۰ (تماس بگیرید) یا قیمت خرید خالی ← بدون محدودیت.</para>
/// <para>محل استفاده: ProductService (ایجاد/ویرایش محصول در پنل) و گزارش «زیر قیمت خرید» در خلاصهٔ روزانه.</para>
/// </summary>
public static class ProfitGuard
{
    public static string? Check(decimal price, decimal? discountPrice, decimal? purchasePrice)
    {
        if (purchasePrice is not > 0 || price <= 0)
            return null;
        var lowest = discountPrice is > 0 && discountPrice < price ? discountPrice.Value : price;
        if (lowest < purchasePrice.Value)
            return $"زیان: کمترین قیمت فروش ({lowest:N0} تومان) کمتر از قیمت خرید ({purchasePrice.Value:N0} تومان) است. اگر حراج عمدی است، «محافظ سود» را در تنظیمات محتوای هوشمند خاموش کنید.";
        // دور ۲۹ (#۱۳): «سر‌به‌سر» هم سود ندارد ← مثل زیان رد می‌شود. مثال: خرید ۱۲۰ هزار و تخفیف ۱۲۰ هزار ← رد.
        return lowest == purchasePrice.Value
            ? $"زیان یا بدون سود: کمترین قیمت فروش ({lowest:N0} تومان) با قیمت خرید برابر است؛ قیمت فروش باید بیشتر از قیمت خرید باشد."
            : null;
    }

    /// <summary>
    /// دور ۲۹ (#۱۳ – «همیشه سود»): آیا زیان از «تخفیف» می‌آید؟ تخفیفِ زیان‌ده یا سر‌به‌سر همیشه رد می‌شود، حتی اگر «محافظ سود» خاموش باشد
    /// (خاموش کردن محافظ فقط برای قیمت پایهٔ حراج عمدی است). مثال: قیمت ۲۰۰، تخفیف ۱۵۰، خرید ۱۵۰ ← true؛ قیمت ۹۰ بدون تخفیف، خرید ۱۲۰ ← false.
    /// </summary>
    public static bool IsDiscountLoss(decimal price, decimal? discountPrice, decimal? purchasePrice) =>
        purchasePrice is > 0 && price > purchasePrice && discountPrice is > 0 && discountPrice < price && discountPrice <= purchasePrice;

    /// <summary>روشن بودن محافظ (پیش‌فرض روشن؛ بدون ردیف تنظیمات هم روشن).</summary>
    public static async Task<bool> IsEnabledAsync(FratakShopDbContext context, CancellationToken cancellationToken = default)
    {
        var json = await context.SmartContentConfigs.OrderBy(c => c.CreatedAt).Select(c => c.OptionsJson).FirstOrDefaultAsync(cancellationToken);
        return SmartConfigService.Parse(json).BlockBelowCost;
    }
}

/// <summary>پیشنهاد سفارش دوباره از تأمین‌کننده (C24).</summary>
public sealed record ReorderSuggestion(Guid ProductId, string Name, int Stock, double DailySales, int DaysLeft, int SuggestedQuantity);

/// <summary>کالای کم‌گردش/راکد (C25).</summary>
public sealed record DeadStockItem(Guid ProductId, string Name, int Stock, int DaysWithoutSale, decimal TiedCapital, bool Critical);

/// <summary>کالای زیر قیمت خرید (C27).</summary>
public sealed record BelowCostItem(Guid ProductId, string Name, decimal SellingPrice, decimal PurchasePrice);

/// <summary>خلاصهٔ روزانهٔ مدیر (C26): «امروز چه کاری مهم است؟» در یک صفحه.</summary>
public sealed class DailyBrief
{
    public int OrdersYesterday { get; init; }
    public decimal RevenueYesterday { get; init; }
    public int OrdersAwaitingAction { get; init; }
    public int SmartNeedsReview { get; init; }
    public int InsightsPending { get; init; }
    public int RemindersSent30Days { get; init; }
    public List<ReorderSuggestion> Reorder { get; init; } = [];
    public List<DeadStockItem> DeadStock { get; init; } = [];
    public List<BelowCostItem> BelowCost { get; init; } = [];
    public List<(string Query, int Count)> UnmetSearches { get; init; } = [];
    public List<string> Actions { get; init; } = [];
}

/// <summary>
/// خلاصهٔ روزانهٔ فروش و موجودی (C24–C27) – فقط خواندنی؛ هیچ چیز را خودکار عوض نمی‌کند.
/// <para>محل استفاده: صفحهٔ «پنل ← محتوای هوشمند ← خلاصهٔ روزانه».</para>
/// </summary>
public interface ISalesBriefService
{
    Task<DailyBrief> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class SalesBriefService(FratakShopDbContext context, ISmartConfigService config) : ISalesBriefService
{
    private static readonly OrderStatus[] Sold = [OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipped, OrderStatus.Delivered];

    public async Task<DailyBrief> GetAsync(CancellationToken cancellationToken = default)
    {
        var options = await config.GetAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var today = Fratak_Shop.Core.Utilities.IranClock.StartOfDay(Fratak_Shop.Core.Utilities.IranClock.Today(now));
        var yesterday = today.AddDays(-1);

        var y = await context.Orders
            .Where(o => o.CreatedAt >= yesterday && o.CreatedAt < today && Sold.Contains(o.Status))
            .GroupBy(o => 1).Select(g => new { Count = g.Count(), Sum = g.Sum(o => o.PayableAmount) })
            .FirstOrDefaultAsync(cancellationToken);
        var awaiting = await context.Orders.CountAsync(o => o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Processing, cancellationToken);
        var needsReview = await context.SmartTasks.CountAsync(t => t.Status == SmartTaskStatus.NeedsReview, cancellationToken);
        var insights = await context.ProductInsights.CountAsync(i => i.Status == ProductInsightStatus.Draft, cancellationToken);
        var since30 = now.AddDays(-30);
        var reminders = await context.CustomerReminders.CountAsync(r => r.Status == CustomerReminderStatus.Sent && r.SentAt >= since30, cancellationToken);

        // فروش ۳۰ روز اخیر هر کالا (یک پرس‌وجوی گروهی).
        var sales = await context.OrderItems
            .Where(i => i.ProductId != null && i.Order.CreatedAt >= since30 && Sold.Contains(i.Order.Status))
            .GroupBy(i => i.ProductId!.Value)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToListAsync(cancellationToken);
        var salesMap = sales.ToDictionary(s => s.ProductId, s => s.Quantity);

        var products = await context.Products
            .Where(p => p.IsActive)
            .Select(p => new
            {
                p.Id, p.Name, p.StockQuantity, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.PurchasePrice, p.CreatedAt,
                LastSale = context.OrderItems.Where(i => i.ProductId == p.Id && Sold.Contains(i.Order.Status)).Max(i => (DateTimeOffset?)i.Order.CreatedAt),
            })
            .Take(5000)
            .ToListAsync(cancellationToken);

        var reorder = new List<ReorderSuggestion>();
        var dead = new List<DeadStockItem>();
        var below = new List<BelowCostItem>();
        foreach (var p in products)
        {
            var sold = salesMap.GetValueOrDefault(p.Id);
            if (Reorder(p.StockQuantity, sold) is { } r)
                reorder.Add(new ReorderSuggestion(p.Id, p.Name, p.StockQuantity, r.Daily, r.DaysLeft, r.Quantity));

            if (p.StockQuantity > 0)
            {
                var reference = p.LastSale ?? p.CreatedAt;
                var days = (int)(now - reference).TotalDays;
                if (days >= options.DeadStockWarnDays)
                    dead.Add(new DeadStockItem(p.Id, p.Name, p.StockQuantity, days, p.StockQuantity * (p.PurchasePrice ?? 0), days >= options.DeadStockCriticalDays));
            }

            var discounted = p.DiscountPrice is > 0 && p.DiscountPrice < p.Price && (p.DiscountStartAt == null || p.DiscountStartAt <= now) && (p.DiscountEndAt == null || p.DiscountEndAt >= now);
            var selling = discounted ? p.DiscountPrice!.Value : p.Price;
            if (p.PurchasePrice is > 0 && selling > 0 && selling < p.PurchasePrice.Value)
                below.Add(new BelowCostItem(p.Id, p.Name, selling, p.PurchasePrice.Value));
        }

        var unmet = await context.SearchQueryLogs
            .Where(q => !q.IsHidden && q.LastResultCount == 0 && q.LastSearchedAt >= now.AddDays(-14))
            .OrderByDescending(q => q.SearchCount).Take(8)
            .Select(q => new { q.Query, q.SearchCount }).ToListAsync(cancellationToken);

        var brief = new DailyBrief
        {
            OrdersYesterday = y?.Count ?? 0,
            RevenueYesterday = y?.Sum ?? 0,
            OrdersAwaitingAction = awaiting,
            SmartNeedsReview = needsReview,
            InsightsPending = insights,
            RemindersSent30Days = reminders,
            Reorder = reorder.OrderBy(r => r.DaysLeft).Take(15).ToList(),
            DeadStock = dead.OrderByDescending(d => d.TiedCapital).ThenByDescending(d => d.DaysWithoutSale).Take(15).ToList(),
            BelowCost = below.OrderBy(b => b.SellingPrice - b.PurchasePrice).Take(20).ToList(),
            UnmetSearches = unmet.Select(u => (u.Query, u.SearchCount)).ToList(),
        };
        brief.Actions.AddRange(Actions(brief));
        return brief;
    }

    /// <summary>
    /// منطق خالص C24: اگر موجودی کمتر از ۱۴ روز فروش است، پیشنهاد سفارش برای ۳۰ روز. فروش کمتر از ۲ عدد در ماه ← پیشنهادی نیست (داده کم).
    /// مثال: ۶۰ فروش در ۳۰ روز (روزی ۲)، موجودی ۱۰ ← ۵ روز مانده، پیشنهاد ۵۰ عدد.
    /// </summary>
    public static (double Daily, int DaysLeft, int Quantity)? Reorder(int stock, int soldLast30Days)
    {
        if (soldLast30Days < 2)
            return null;
        var daily = soldLast30Days / 30.0;
        var daysLeft = (int)Math.Floor(Math.Max(0, stock) / daily);
        if (daysLeft >= 14)
            return null;
        var quantity = (int)Math.Ceiling(daily * 30) - Math.Max(0, stock);
        return quantity <= 0 ? null : (Math.Round(daily, 1), daysLeft, quantity);
    }

    /// <summary>جمله‌های «کار امروز» به ترتیب اهمیت (فقط برای مواردی که واقعاً وجود دارند).</summary>
    public static List<string> Actions(DailyBrief brief)
    {
        var list = new List<string>();
        if (brief.BelowCost.Count > 0)
            list.Add($"{PersianText.Digits(brief.BelowCost.Count)} کالا زیر قیمت خرید فروخته می‌شود؛ قیمتشان را بررسی کنید.");
        if (brief.Reorder.Any(r => r.DaysLeft <= 3))
            list.Add($"موجودی {PersianText.Digits(brief.Reorder.Count(r => r.DaysLeft <= 3))} کالای پرفروش تا ۳ روز دیگر تمام می‌شود؛ سفارش دهید.");
        if (brief.OrdersAwaitingAction > 0)
            list.Add($"{PersianText.Digits(brief.OrdersAwaitingAction)} سفارش منتظر آماده‌سازی/ارسال است.");
        if (brief.SmartNeedsReview > 0)
            list.Add($"{PersianText.Digits(brief.SmartNeedsReview)} پیشنهاد هوشمند منتظر تأیید شماست.");
        if (brief.InsightsPending > 0)
            list.Add($"{PersianText.Digits(brief.InsightsPending)} خلاصهٔ خرید منتظر تأیید است.");
        if (brief.DeadStock.Any(d => d.Critical))
            list.Add($"{PersianText.Digits(brief.DeadStock.Count(d => d.Critical))} کالا مدت زیادی فروش نداشته؛ تخفیف یا معرفی در بلاگ را در نظر بگیرید.");
        if (brief.UnmetSearches.Count > 0)
            list.Add($"مشتری‌ها «{brief.UnmetSearches[0].Query}» را جست‌وجو کرده‌اند و چیزی پیدا نکرده‌اند؛ شاید ارزش افزودن داشته باشد.");
        return list;
    }
}
