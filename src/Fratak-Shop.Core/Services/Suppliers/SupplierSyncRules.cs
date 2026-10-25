using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Core.Services.Suppliers;

/// <summary>تنظیمات قوانین مقایسه (از تنظیمات فروشگاه خوانده می‌شود).</summary>
/// <param name="AutoOutOfStock">ناموجودی منبع خودکار به فروشگاه اعمال شود.</param>
/// <param name="MarginPercent">موجودی پیشنهادی چند درصد کمتر از منبع باشد.</param>
/// <param name="MaxQuantity">سقف موجودی پیشنهادی.</param>
public sealed record SupplierSyncPolicy(bool AutoOutOfStock, int MarginPercent, int MaxQuantity)
{
    public static SupplierSyncPolicy Default { get; } = new(true, 20, 10);
}

/// <summary>وضعیت فعلی محصول ما که با منبع مقایسه می‌شود.</summary>
/// <param name="Stock">موجودی فعلی فروشگاه.</param>
/// <param name="PurchasePrice">قیمت خرید ثبت‌شده (تومان).</param>
/// <param name="AutoOutOfStock">آیا موجودی صفر را خود سیستم گذاشته است.</param>
/// <param name="ConsecutiveErrors">تعداد خطاهای پشت‌سرهم پیش از این بررسی.</param>
public sealed record SupplierProductView(int Stock, decimal? PurchasePrice, bool AutoOutOfStock, int ConsecutiveErrors);

/// <summary>یک تصمیم برای یک نوع تغییر.</summary>
public enum SupplierDecisionAction
{
    /// <summary>پیشنهاد «منتظر تأیید» ساخته یا به‌روز شود.</summary>
    Propose,

    /// <summary>سیستم همین الان اعمال کند (فقط کارهای ایمن: صفر یا کم کردن موجودی).</summary>
    AutoApply,

    /// <summary>پیشنهاد منتظرِ این نوع دیگر معتبر نیست و کنار برود.</summary>
    Supersede,
}

/// <summary>
/// خروجی قوانین برای یک نوع تغییر. مثال: (PriceChange, Propose, Old=400000, New=415000).
/// </summary>
public sealed record SupplierDecision(SupplierChangeKind Kind, SupplierDecisionAction Action, decimal? OldValue = null, decimal? NewValue = null, string? Note = null);

/// <summary>
/// قوانین خالص مقایسهٔ منبع با فروشگاه (بدون دیتابیس، کاملاً تست‌پذیر).
/// <para>اصل ایمنی: فقط کارهایی خودکار انجام می‌شوند که جلوی «فروش کالای ناموجود» را بگیرند (صفر یا کم کردن موجودی).
/// افزایش موجودی و تغییر قیمت همیشه با تأیید کارمند است.</para>
/// </summary>
public static class SupplierSyncRules
{
    /// <summary>تعداد خطای پشت‌سرهم که از آن به بعد در گزارش «مشکل‌ها» پیشنهاد ساخته می‌شود.</summary>
    public const int ErrorReportThreshold = 3;

    /// <summary>
    /// موجودی پیشنهادی ما بر اساس موجودی منبع: همیشه کمتر از منبع (تا بیش از موجودی واقعی نفروشیم) و حداکثر تا سقف.
    /// <list type="bullet">
    /// <item>منبع عدد اعلام نکرده (فقط «موجود»): ۱ عدد (محتاطانه).</item>
    /// <item>منبع ۵۰ عدد، حاشیهٔ ۲۰٪، سقف ۱۰ → ۱۰ (۴۰ از سقف بیشتر است).</item>
    /// <item>منبع ۵ عدد، حاشیهٔ ۲۰٪ → ۴.  منبع ۱ عدد، حاشیهٔ ۲۰٪ → ۰ (یک عدد آخر را نمی‌فروشیم).</item>
    /// </list>
    /// </summary>
    public static int SuggestStock(int? sourceStock, int marginPercent, int maxQuantity)
    {
        var max = Math.Max(1, maxQuantity);
        if (sourceStock is null)
            return 1;
        if (sourceStock <= 0)
            return 0;
        var margin = Math.Clamp(marginPercent, 0, 90);
        var source = sourceStock.Value;
        var value = (int)Math.Floor(source * (100 - margin) / 100m);
        if (margin > 0 && value >= source)
            value = source - 1;
        return Math.Clamp(value, 0, max);
    }

    /// <summary>
    /// مقایسهٔ یک محصول با نتیجهٔ منبع و تولید تصمیم‌ها. ترتیب مهم نیست؛ هر نوع حداکثر یک تصمیم دارد.
    /// <para>مثال: منبع ناموجود و موجودی ما ۳ → (OutOfStock, AutoApply, 3 → 0) و Supersede برای BackInStock/StockIncrease.</para>
    /// </summary>
    public static List<SupplierDecision> Evaluate(SupplierSnapshot snapshot, SupplierProductView ours, SupplierSyncPolicy policy)
    {
        var decisions = new List<SupplierDecision>();
        switch (snapshot.Status)
        {
            case SupplierItemStatus.Error:
                // وضعیت واقعی معلوم نیست؛ هیچ تغییری روی موجودی نمی‌دهیم. فقط خطای مکرر گزارش می‌شود.
                if (ours.ConsecutiveErrors + 1 >= ErrorReportThreshold)
                    decisions.Add(new(SupplierChangeKind.Error, SupplierDecisionAction.Propose, Note: Trim(snapshot.Error)));
                return decisions;

            case SupplierItemStatus.NotFound:
                // ممکن است لینک عوض شده باشد؛ کالا را خودکار ناموجود نمی‌کنیم تا کارمند بررسی کند.
                decisions.Add(new(SupplierChangeKind.NotFound, SupplierDecisionAction.Propose, Note: Trim(snapshot.Error)));
                decisions.Add(new(SupplierChangeKind.Error, SupplierDecisionAction.Supersede));
                return decisions;
        }

        // خواندن موفق بود: گزارش‌های «پیدا نشد» و «خطا» دیگر معتبر نیستند.
        decisions.Add(new(SupplierChangeKind.NotFound, SupplierDecisionAction.Supersede));
        decisions.Add(new(SupplierChangeKind.Error, SupplierDecisionAction.Supersede));

        if (snapshot.Status == SupplierItemStatus.OutOfStock)
        {
            decisions.Add(new(SupplierChangeKind.BackInStock, SupplierDecisionAction.Supersede));
            decisions.Add(new(SupplierChangeKind.StockIncrease, SupplierDecisionAction.Supersede));
            decisions.Add(new(SupplierChangeKind.StockDecrease, SupplierDecisionAction.Supersede));
            if (ours.Stock > 0)
                decisions.Add(new(SupplierChangeKind.OutOfStock,
                    policy.AutoOutOfStock ? SupplierDecisionAction.AutoApply : SupplierDecisionAction.Propose,
                    ours.Stock, 0));
            else
                decisions.Add(new(SupplierChangeKind.OutOfStock, SupplierDecisionAction.Supersede));
        }
        else if (snapshot.Status == SupplierItemStatus.InStock)
        {
            decisions.Add(new(SupplierChangeKind.OutOfStock, SupplierDecisionAction.Supersede));
            var suggested = SuggestStock(snapshot.Stock, policy.MarginPercent, policy.MaxQuantity);

            if (ours.Stock <= 0)
            {
                // کالای ناموجود ما در منبع موجود شد.
                decisions.Add(suggested >= 1
                    ? new(SupplierChangeKind.BackInStock, SupplierDecisionAction.Propose, 0, suggested)
                    : new(SupplierChangeKind.BackInStock, SupplierDecisionAction.Supersede));
                decisions.Add(new(SupplierChangeKind.StockIncrease, SupplierDecisionAction.Supersede));
                decisions.Add(new(SupplierChangeKind.StockDecrease, SupplierDecisionAction.Supersede));
            }
            else
            {
                decisions.Add(new(SupplierChangeKind.BackInStock, SupplierDecisionAction.Supersede));
                if (snapshot.Stock is { } source && ours.Stock > Math.Max(suggested, 0) && ours.Stock >= source)
                {
                    // موجودی ما از منبع بیشتر یا برابر است ← خطر فروش کالایی که منبع ندارد.
                    decisions.Add(new(SupplierChangeKind.StockDecrease,
                        policy.AutoOutOfStock ? SupplierDecisionAction.AutoApply : SupplierDecisionAction.Propose,
                        ours.Stock, suggested, $"موجودی منبع: {source:N0}"));
                    decisions.Add(new(SupplierChangeKind.StockIncrease, SupplierDecisionAction.Supersede));
                }
                else
                {
                    decisions.Add(new(SupplierChangeKind.StockDecrease, SupplierDecisionAction.Supersede));
                    decisions.Add(ours.Stock < suggested
                        ? new(SupplierChangeKind.StockIncrease, SupplierDecisionAction.Propose, ours.Stock, suggested)
                        : new(SupplierChangeKind.StockIncrease, SupplierDecisionAction.Supersede));
                }
            }
        }

        // قیمت (هم برای موجود و هم ناموجود، اگر سایت اعلام کرده باشد).
        if (snapshot.Price is > 0 && snapshot.Price != ours.PurchasePrice)
            decisions.Add(new(SupplierChangeKind.PriceChange, SupplierDecisionAction.Propose, ours.PurchasePrice, snapshot.Price));
        else if (snapshot.Price is > 0)
            decisions.Add(new(SupplierChangeKind.PriceChange, SupplierDecisionAction.Supersede));

        return decisions;
    }

    /// <summary>عنوان فارسی نوع تغییر.</summary>
    public static string KindTitle(SupplierChangeKind kind) => kind switch
    {
        SupplierChangeKind.OutOfStock => "ناموجود شد",
        SupplierChangeKind.BackInStock => "دوباره موجود شد",
        SupplierChangeKind.StockIncrease => "افزایش موجودی",
        SupplierChangeKind.StockDecrease => "کاهش موجودی",
        SupplierChangeKind.PriceChange => "تغییر قیمت",
        SupplierChangeKind.NotFound => "در منبع پیدا نشد",
        SupplierChangeKind.Error => "خطای خواندن",
        _ => kind.ToString(),
    };

    /// <summary>عنوان فارسی وضعیت منبع.</summary>
    public static string StatusTitle(SupplierItemStatus status) => status switch
    {
        SupplierItemStatus.InStock => "موجود",
        SupplierItemStatus.OutOfStock => "ناموجود",
        SupplierItemStatus.NotFound => "پیدا نشد",
        SupplierItemStatus.Error => "خطا",
        _ => "بررسی نشده",
    };

    /// <summary>آیا تأیید این نوع موجودی فروشگاه را تغییر می‌دهد؟</summary>
    public static bool IsStockKind(SupplierChangeKind kind) =>
        kind is SupplierChangeKind.OutOfStock or SupplierChangeKind.BackInStock or SupplierChangeKind.StockIncrease or SupplierChangeKind.StockDecrease;

    private static string? Trim(string? text) => text is null ? null : text.Length <= 480 ? text : text[..480];
}

/// <summary>
/// زمان‌بندی اجراها: N بار در شبانه‌روز با فاصلهٔ مساوی از نیمه‌شب به وقت ایران.
/// مثال: ۴ بار → ۰۰:۰۰، ۰۶:۰۰، ۱۲:۰۰، ۱۸:۰۰. ۵ بار → ۰۰:۰۰، ۰۴:۴۸، ۰۹:۳۶، ۱۴:۲۴، ۱۹:۱۲.
/// </summary>
public static class SupplierSyncSchedule
{
    /// <summary>آخرین نوبتی که زمانش رسیده (≤ now).</summary>
    public static DateTimeOffset LatestSlot(DateTimeOffset now, int runsPerDay)
    {
        var runs = Math.Clamp(runsPerDay, 1, 24);
        var today = IranClock.Today(now);
        var start = IranClock.StartOfDay(today);
        var interval = TimeSpan.FromMinutes(24 * 60.0 / runs);
        var elapsed = now - start;
        var index = Math.Clamp((int)Math.Floor(elapsed / interval), 0, runs - 1);
        return start + interval * index;
    }

    /// <summary>نوبت بعدی (&gt; now).</summary>
    public static DateTimeOffset NextSlot(DateTimeOffset now, int runsPerDay)
    {
        var runs = Math.Clamp(runsPerDay, 1, 24);
        var latest = LatestSlot(now, runs);
        var next = latest + TimeSpan.FromMinutes(24 * 60.0 / runs);
        var tomorrow = IranClock.StartOfDay(IranClock.Today(now).AddDays(1));
        return next > tomorrow ? tomorrow : next;
    }

    /// <summary>
    /// آیا اجرای زمان‌بندی‌شده لازم است؟ وقتی آخرین اجرای زمان‌بندی‌شده پیش از آخرین نوبت رسیده بوده.
    /// مثال: ساعت ۱۲:۰۵، آخرین اجرا ۰۶:۰۱ → بله. آخرین اجرا ۱۲:۰۰:۳۰ → خیر.
    /// </summary>
    public static bool IsDue(DateTimeOffset now, int runsPerDay, DateTimeOffset? lastScheduledStart) =>
        lastScheduledStart is null || lastScheduledStart.Value < LatestSlot(now, runsPerDay);
}
