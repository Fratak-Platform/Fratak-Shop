using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Analytics;

// ═══════════════════════════════════════════════════════════════════════════
//  آمار بازدید داخلی (مرحلهٔ هفتم – فاز ۷) – بدون Google Analytics یا هر سرویس خارجی.
//  • VisitorDay      : یک ردیف برای هر «بازدیدکننده در هر روز» (به وقت ایران) با مراحل قیف خرید و منبع ورودی.
//  • AnalyticsCounter: شمارندهٔ روزانهٔ جمعی (مثلاً بازدید هر محصول یا افزودن هر محصول به سبد).
//  جمع‌آوری: Web/Infrastructure/Analytics (بافر در حافظه و ذخیرهٔ دقیقه‌ای)؛
//  ذخیره: Core/Services/Reports/AnalyticsService.cs؛ گزارش: Core/Services/Reports/AnalyticsReportService.cs.
//  حریم خصوصی: IP ذخیره نمی‌شود؛ هر مرورگر با یک شناسهٔ تصادفی در کوکی شناخته می‌شود.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// مراحل قیف خرید که یک بازدیدکننده در یک روز طی کرده (پرچم بیتی؛ چند مرحله با هم جمع می‌شوند).
/// <para>مثال: کسی که صفحه‌ای دیده، محصولی باز کرده و به سبد افزوده → Visit | ProductView | AddToCart = 7.</para>
/// مراحل «ثبت سفارش» و «پرداخت» از جدول سفارش‌ها (ستون VisitorKey) خوانده می‌شوند، نه از اینجا.
/// </summary>
[Flags]
public enum FunnelStage
{
    None = 0,

    /// <summary>دیدن هر صفحه از سایت.</summary>
    Visit = 1,

    /// <summary>باز کردن صفحهٔ یک محصول.</summary>
    ProductView = 2,

    /// <summary>افزودن کالا به سبد خرید.</summary>
    AddToCart = 4,

    /// <summary>ورود به صفحهٔ تسویه حساب.</summary>
    Checkout = 8,
}

/// <summary>
/// خلاصهٔ رفتار یک بازدیدکننده در یک روز. کلید یکتا: (Day, VisitorKey).
/// <para>منبع ورودی همان «اولین منبع آن روز» است (اگر صبح از گوگل و عصر مستقیم آمد، «گوگل» می‌ماند).</para>
/// </summary>
public class VisitorDay : BaseEntity
{
    /// <summary>روز به وقت ایران.</summary>
    public DateOnly Day { get; set; }

    /// <summary>شناسهٔ تصادفی مرورگر (هش‌شدهٔ کوکی fr_vid). مثال: «3f9a…» (۳۲ نویسه).</summary>
    [MaxLength(32)]
    public string VisitorKey { get; set; } = null!;

    /// <summary>اگر وارد حساب بود، شناسهٔ کاربر (برای «بازدیدکنندهٔ عضو/مهمان»).</summary>
    public Guid? UserId { get; set; }

    /// <summary>جمع بیتی مراحل <see cref="FunnelStage"/>.</summary>
    public int Stages { get; set; }

    /// <summary>تعداد صفحه‌های دیده‌شده در این روز.</summary>
    public int PageViews { get; set; }

    /// <summary>ساعت اولین بازدید (۰ تا ۲۳ به وقت ایران) برای نقشهٔ حرارتی روز × ساعت.</summary>
    public byte FirstHour { get; set; }

    /// <summary>mobile / tablet / desktop.</summary>
    [MaxLength(10)]
    public string Device { get; set; } = "desktop";

    /// <summary>منبع ورودی. مثال: «google»، «instagram»، «torob»، «direct».</summary>
    [MaxLength(60)]
    public string Source { get; set; } = "direct";

    /// <summary>نوع منبع. مثال: «organic»، «social»، «referral»، «cpc»، «direct».</summary>
    [MaxLength(30)]
    public string Medium { get; set; } = "direct";

    /// <summary>نام کمپین (utm_campaign). مثال: «yalda-1405».</summary>
    [MaxLength(100)]
    public string? Campaign { get; set; }

    /// <summary>اولین صفحه‌ای که در این روز دید. مثال: «/product/…».</summary>
    [MaxLength(300)]
    public string? LandingPath { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}

/// <summary>
/// شمارندهٔ روزانهٔ جمعی. کلید یکتا: (Day, Metric, Key).
/// <para>مثال: (۱۴۰۵/۰۸/۰۴، «pv»، شناسهٔ محصول) = ۴۲ یعنی آن محصول آن روز ۴۲ بار دیده شده.</para>
/// متریک‌ها در <see cref="AnalyticsMetrics"/> تعریف شده‌اند.
/// </summary>
public class AnalyticsCounter : BaseEntity
{
    public DateOnly Day { get; set; }

    [MaxLength(20)]
    public string Metric { get; set; } = null!;

    [MaxLength(64)]
    public string Key { get; set; } = null!;

    public int Count { get; set; }
}

/// <summary>نام متریک‌های <see cref="AnalyticsCounter"/>. نام‌ها در دیتابیس ذخیره می‌شوند؛ عوض نکنید.</summary>
public static class AnalyticsMetrics
{
    /// <summary>بازدید صفحهٔ محصول؛ Key = شناسهٔ محصول (N).</summary>
    public const string ProductView = "pv";

    /// <summary>افزودن محصول به سبد؛ Key = شناسهٔ محصول (N).</summary>
    public const string AddToCart = "cart";
}
