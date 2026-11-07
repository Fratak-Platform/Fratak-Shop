using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Smart;

/// <summary>
/// نوع کار «رشد خودکار» (دور ۲۱). مثال: <see cref="TrafficDrop"/> = بازدید امروز خیلی کمتر از معمول بود و سامانه خودش اقدام کرد.
/// </summary>
public enum GrowthActionKind : byte
{
    None = 0,

    /// <summary>اجرای روزانه (یک ردیف در روز؛ نشانهٔ «امروز اجرا شد»).</summary>
    DailyRun = 1,

    /// <summary>بالا بردن نمرهٔ سئوی ضعیف‌ترین صفحه‌ها.</summary>
    SeoLift = 2,

    /// <summary>افت بازدید تشخیص داده شد.</summary>
    TrafficDrop = 3,

    /// <summary>جلو انداختن مقالهٔ بعدی تقویم بلاگ.</summary>
    BlogBoost = 4,

    /// <summary>ساخت ایدهٔ بنر تازه برای تأیید مدیر.</summary>
    BannerIdeas = 5,

    /// <summary>تکمیل کالاهای پربازدیدِ ناقص.</summary>
    ProductRefresh = 6,
}

/// <summary>
/// گزارش کارهای «رشد خودکار»: هر کاری که سامانه بدون انسان انجام داد با خلاصهٔ فارسی ثبت می‌شود تا مدیر فقط نظارت کند.
/// <para>محل استفاده: Core/Services/Smart/GrowthAutopilot.cs و صفحهٔ پنل /admin/smart/growth.</para>
/// </summary>
public class GrowthAction : BaseEntity
{
    /// <summary>روز (به وقت ایران) که کار برای آن انجام شد.</summary>
    public DateOnly Day { get; set; }

    public GrowthActionKind Kind { get; set; }

    /// <summary>تعداد موردها (صفحه، کالا، ایده …).</summary>
    public int Count { get; set; }

    /// <summary>عدد اصلی (مثلاً نمرهٔ میانگین سئو یا بازدید روز).</summary>
    public int? Metric { get; set; }

    [MaxLength(1000)]
    public string Summary { get; set; } = "";
}
