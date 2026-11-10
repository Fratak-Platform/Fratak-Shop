using System.Security.Cryptography;
using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Marketing;

/// <summary>
/// احتمال ریزش یک مشتری و پیشنهاد هدیه (برای پنل). مثال: (۰٫۸۲، فاصلهٔ معمول ۳۰ روز، ۹۵ روز از آخرین خرید، ۳ کالای پیشنهادی).
/// </summary>
public sealed record ChurnInsight(double Probability, int ExpectedGapDays, int DaysSinceLastOrder, IReadOnlyList<ProductFilterData> Gifts)
{
    /// <summary>درصد برای نمایش.</summary>
    public int Percent => (int)Math.Round(Probability * 100);

    /// <summary>در خطر جدی ریزش (مبنای پیام برگشت).</summary>
    public bool AtRisk => Probability >= ChurnModel.WinBackThreshold;
}

/// <summary>
/// دور ۲۴ (پیشنهاد ۵۷): مدل ریزش مشتری بر اساس «ریتم خرید خودِ هر مشتری».
/// <para>
/// هر مشتری فاصلهٔ معمول بین خریدهایش را دارد (کسی هر ۲ هفته می‌خرد، کسی هر ۶ ماه). فاصلهٔ شخصی با فاصلهٔ معمول کل فروشگاه
/// هموار می‌شود (مشتری کم‌خرید دادهٔ کمی دارد) و احتمال ریزش با تابع ویبول (k=۲) از «روزهای گذشته ÷ فاصلهٔ معمول» به دست می‌آید:
/// <c>P = 1 − exp(−(t/g)²/2)</c>. یعنی: هم‌اندازهٔ فاصلهٔ معمول ← ۳۹٪، دو برابر ← ۸۶٪، نصف ← ۱۲٪.
/// </para>
/// <para>مثال: مشتری با ۵ خرید ماهانه که ۷۵ روز نیامده ← حدود ۹۵٪ (وقت پیام برگشت)؛ همان مشتری بعد از ۱۵ روز ← ۱۲٪.</para>
/// <para>محل استفاده: <see cref="WinBackService"/> و صفحهٔ جزئیات مشتری در پنل.</para>
/// </summary>
public static class ChurnModel
{
    /// <summary>از این احتمال به بالا پیام برگشت (در صورت روشن بودن) فرستاده می‌شود.</summary>
    public const double WinBackThreshold = 0.75;

    /// <summary>وزن فاصلهٔ فروشگاه در هموارسازی (معادل چند «فاصلهٔ فرضی»).</summary>
    public const double PriorWeight = 2;

    /// <summary>
    /// فاصلهٔ معمول هموارشدهٔ یک مشتری (روز). مثال: ۵ خرید در ۱۲۰ روز (۴ فاصلهٔ ۳۰ روزه) و فاصلهٔ فروشگاه ۶۰ ← (۴×۳۰+۲×۶۰)÷۶ = ۴۰.
    /// </summary>
    public static double ExpectedGap(int orderCount, DateTimeOffset firstAt, DateTimeOffset lastAt, double shopGapDays)
    {
        var gaps = Math.Max(0, orderCount - 1);
        var shop = Math.Clamp(shopGapDays, 7, 365);
        if (gaps == 0)
            return shop;
        var personal = Math.Max(1, (lastAt - firstAt).TotalDays / gaps);
        return Math.Clamp((gaps * personal + PriorWeight * shop) / (gaps + PriorWeight), 7, 365);
    }

    /// <summary>احتمال ریزش. مثال: Probability(60, 30) ≈ ۰٫۸۶.</summary>
    public static double Probability(double daysSinceLast, double expectedGapDays)
    {
        if (daysSinceLast <= 0 || expectedGapDays <= 0)
            return 0;
        var ratio = daysSinceLast / expectedGapDays;
        return Math.Round(1 - Math.Exp(-ratio * ratio / 2), 3);
    }

    /// <summary>
    /// مبلغ کوپن برگشت با سقف: درصد × میانگین خرید مشتری، حداکثر <paramref name="maxAmount"/>، گرد به هزار تومان.
    /// مثال: ۱۰٪ و میانگین ۸۰۰ هزار ← سقف تخفیف ۸۰ هزار؛ میانگین ۵ میلیون و سقف ۲۰۰ هزار ← ۲۰۰ هزار.
    /// </summary>
    public static decimal CapFor(int percent, decimal averageOrder, decimal maxAmount)
    {
        var raw = Math.Max(0, averageOrder) * Math.Clamp(percent, 1, 50) / 100m;
        var capped = Math.Min(raw, Math.Max(0, maxAmount));
        return Math.Max(10_000m, Math.Floor(capped / 1000m) * 1000m);
    }
}

/// <summary>
/// دور ۲۴ (پیشنهاد ۵۷): برگرداندن مشتری‌های در حال ریزش با کد تخفیف شخصی سقف‌دار و پیشنهاد هدیهٔ شخصی‌سازی‌شده.
/// <list type="bullet">
/// <item>فقط وقتی «پیام برگشت» در تنظیمات هوشمند روشن است (پیش‌فرض خاموش؛ چون هزینه دارد).</item>
/// <item>فقط بین ساعت ۱۰ تا ۲۰ ایران، حداکثر <see cref="SmartOptions.WinBackPerDay"/> مشتری در روز، هر مشتری حداکثر یک‌بار در ۱۲۰ روز.</item>
/// <item>فقط اعلان داخل سایت و پوش (بدون پیامک؛ مزاحمت و هزینه ندارد). کد یک‌بارمصرف، شخصی، با سقف مبلغ و حداقل خرید.</item>
/// <item>هدیه: ۳ کالای مرتبط با خریدهای قبلی (فیلترینگ مشارکتی) در متن پیام.</item>
/// </list>
/// </summary>
public interface IWinBackService
{
    /// <summary>بینش ریزش یک مشتری برای پنل (بدون خرید ← null).</summary>
    Task<ChurnInsight?> InsightAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>اجرای خودکار (هر ساعت حداکثر یک‌بار مؤثر). خروجی = تعداد پیام ارسال‌شده.</summary>
    Task<int> RunAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class WinBackService(
    FratakShopDbContext context,
    ISmartConfigService smartConfig,
    IRecommendationService recommendations,
    INotificationService notifications) : IWinBackService
{
    /// <summary>پیشوند کد کوپن برگشت.</summary>
    public const string CodePrefix = "WB-";

    /// <summary>فاصلهٔ کمینه بین دو پیام برگشت به یک مشتری.</summary>
    public const int CooldownDays = 120;

    private static DateTimeOffset _lastRun = DateTimeOffset.MinValue;
    private static (double Gap, DateTimeOffset At)? _shopGap;
    private static readonly object GapLock = new();

    private sealed record Buyer(Guid UserId, int Orders, DateTimeOffset First, DateTimeOffset Last, decimal Spent);

    private IQueryable<Order> Successful() => context.Orders.AsNoTracking()
        .Where(o => o.UserId != null && o.PaymentStatus == PaymentStatus.Paid && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned);

    /// <inheritdoc />
    public async Task<ChurnInsight?> InsightAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var mine = Successful().Where(o => o.UserId == userId);
        var count = await mine.CountAsync(cancellationToken);
        if (count == 0)
            return null;
        var first = await mine.MinAsync(o => o.CreatedAt, cancellationToken);
        var last = await mine.MaxAsync(o => o.CreatedAt, cancellationToken);
        var gap = ChurnModel.ExpectedGap(count, first, last, await ShopGapAsync(cancellationToken));
        var days = Math.Max(0, (DateTimeOffset.UtcNow - last).TotalDays);
        var gifts = await GiftsAsync(userId, cancellationToken);
        return new ChurnInsight(ChurnModel.Probability(days, gap), (int)Math.Round(gap), (int)days, gifts);
    }

    /// <inheritdoc />
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var options = await smartConfig.GetAsync(cancellationToken);
        if (!options.WinBackEnabled || now - _lastRun < TimeSpan.FromHours(1))
            return 0;
        var hour = IranClock.ToLocal(now).Hour;
        if (hour is < 10 or >= 20)
            return 0;
        _lastRun = now;

        var todayStart = IranClock.StartOfDay(IranClock.Today(now));
        var sentToday = await context.Coupons.IgnoreQueryFilters().CountAsync(c => c.Code.StartsWith(CodePrefix) && c.CreatedAt >= todayStart, cancellationToken);
        var budget = options.WinBackPerDay - sentToday;
        if (budget <= 0)
            return 0;

        var shopGap = await ShopGapAsync(cancellationToken);
        var oldest = now.AddDays(-365);
        var newest = now.AddDays(-21);
        var rows = await Successful()
            .GroupBy(o => o.UserId!.Value)
            .Select(g => new { UserId = g.Key, Orders = g.Count(), First = g.Min(o => o.CreatedAt), Last = g.Max(o => o.CreatedAt), Spent = g.Sum(o => o.PayableAmount - o.RefundedAmount) })
            .Where(b => b.Last >= oldest && b.Last <= newest)
            .OrderByDescending(b => b.Spent)
            .Take(3000)
            .ToListAsync(cancellationToken);
        var buyers = rows.Select(r => new Buyer(r.UserId, r.Orders, r.First, r.Last, r.Spent)).ToList();

        var candidates = buyers
            .Select(b => (Buyer: b, Probability: ChurnModel.Probability((now - b.Last).TotalDays, ChurnModel.ExpectedGap(b.Orders, b.First, b.Last, shopGap))))
            .Where(x => x.Probability >= ChurnModel.WinBackThreshold)
            .OrderByDescending(x => x.Buyer.Spent * (decimal)x.Probability)
            .Select(x => x.Buyer)
            .Take(200)
            .ToList();
        if (candidates.Count == 0)
            return 0;

        var ids = candidates.Select(c => c.UserId).ToList();
        var since = now.AddDays(-CooldownDays);
        var contacted = await context.Coupons.IgnoreQueryFilters()
            .Where(c => c.UserId != null && ids.Contains(c.UserId.Value) && c.Code.StartsWith(CodePrefix) && c.CreatedAt >= since)
            .Select(c => c.UserId!.Value).Distinct().ToListAsync(cancellationToken);
        var blocked = await context.Users.Where(u => ids.Contains(u.Id) && u.IsBlocked).Select(u => u.Id).ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var buyer in candidates.Where(c => !contacted.Contains(c.UserId) && !blocked.Contains(c.UserId)).Take(budget))
        {
            await SendAsync(buyer, options, now, cancellationToken);
            sent++;
        }
        return sent;
    }

    private async Task SendAsync(Buyer buyer, SmartOptions options, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var average = buyer.Orders > 0 ? buyer.Spent / buyer.Orders : 0;
        var cap = ChurnModel.CapFor(options.WinBackPercent, average, options.WinBackMaxAmount);
        string code;
        do
        {
            code = CodePrefix + RandomCode(6);
        }
        while (await context.Coupons.IgnoreQueryFilters().AnyAsync(c => c.Code == code, cancellationToken));

        context.Coupons.Add(new Coupon
        {
            Code = code,
            Title = "کد برگشت – مشتری عزیز",
            Description = "کد شخصی و یک‌بارمصرف دلتنگی (پیام برگشت خودکار).",
            Type = CouponType.Percentage,
            Value = options.WinBackPercent,
            MaxDiscountAmount = cap,
            MinOrderAmount = Math.Floor(Math.Max(0, average) * 0.5m / 1000m) * 1000m,
            StartsAt = now,
            EndsAt = now.AddDays(options.WinBackDays),
            UsageLimit = 1,
            UsageLimitPerUser = 1,
            UserId = buyer.UserId,
            IsPublic = false,
            IsActive = true,
        });
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        var gifts = await GiftsAsync(buyer.UserId, cancellationToken);
        var giftText = gifts.Count == 0 ? "" : " پیشنهاد ویژه برای شما: " + string.Join("، ", gifts.Select(g => "«" + g.Name + "»")) + ".";
        await notifications.NotifyAsync(new NotificationRequest
        {
            UserId = buyer.UserId,
            Category = NotificationCategory.Promotion,
            LinkUrl = gifts.Count > 0 ? "/product/" + gifts[0].Slug : "/products",
            Content = new NotificationContent
            {
                Title = "دلمان برایتان تنگ شده!",
                Body = $"یک کد تخفیف {options.WinBackPercent}٪ (تا سقف {cap:N0} تومان) فقط برای شما: {code} – تا {options.WinBackDays} روز معتبر است.{giftText}",
                LinkText = gifts.Count > 0 ? "دیدن پیشنهاد" : "دیدن محصولات",
                SmsText = null,
            },
        });
    }

    /// <summary>هدیهٔ پیشنهادی: ۳ کالای موجود مرتبط با آخرین خریدها (فیلترینگ مشارکتی)، وگرنه پیشنهاد شخصی.</summary>
    private async Task<List<ProductFilterData>> GiftsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var lastProducts = await context.OrderItems.AsNoTracking()
            .Where(i => i.ProductId != null && i.Order.UserId == userId && i.Order.PaymentStatus == PaymentStatus.Paid)
            .OrderByDescending(i => i.Order.CreatedAt)
            .Select(i => i.ProductId!.Value)
            .Take(10)
            .ToListAsync(cancellationToken);
        var list = await recommendations.GetForCartAsync(lastProducts.Distinct().ToList(), userId, 6);
        return list.Where(p => !lastProducts.Contains(p.Id)).Take(3).ToList();
    }

    /// <summary>میانهٔ فاصلهٔ خرید مشتری‌های چندبار-خرید (روز)؛ حافظهٔ ۱۲ ساعته؛ بی‌داده ← ۶۰.</summary>
    private async Task<double> ShopGapAsync(CancellationToken cancellationToken)
    {
        lock (GapLock)
        {
            if (_shopGap is { } cached && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromHours(12))
                return cached.Gap;
        }
        var rows = await Successful()
            .GroupBy(o => o.UserId!.Value)
            .Select(g => new { Count = g.Count(), First = g.Min(o => o.CreatedAt), Last = g.Max(o => o.CreatedAt) })
            .Where(x => x.Count >= 2)
            .Take(5000)
            .ToListAsync(cancellationToken);
        var gaps = rows.Select(r => (r.Last - r.First).TotalDays / (r.Count - 1)).Where(g => g >= 1).OrderBy(g => g).ToList();
        var gap = gaps.Count < 10 ? 60 : gaps[gaps.Count / 2];
        lock (GapLock)
            _shopGap = (gap, DateTimeOffset.UtcNow);
        return gap;
    }

    private static string RandomCode(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(length, alphabet, (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        });
    }
}
