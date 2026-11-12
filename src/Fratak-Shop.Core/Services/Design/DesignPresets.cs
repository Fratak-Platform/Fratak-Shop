using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Data.Entities.Marketing;

namespace Fratak_Shop.Core.Services.Design;

/// <summary>
/// یک اندازهٔ آماده برای طرح. مثال: «hero» = اسلایدر اصلی صفحهٔ نخست، ۱۹۲۰×۴۸۰ پیکسل (دور ۲۳).
/// </summary>
/// <param name="Key">کلید ثابت (در DesignDoc.PresetKey ذخیره می‌شود).</param>
/// <param name="Title">نام فارسی برای پنل.</param>
/// <param name="Kind">نوع طرح.</param>
/// <param name="Width">عرض به پیکسل.</param>
/// <param name="Height">ارتفاع به پیکسل.</param>
/// <param name="Position">جایگاه بنر در صفحهٔ نخست (فقط بنرها).</param>
/// <param name="Hint">راهنمای کوتاه طراحی همان جایگاه (در ویرایشگر و فرم ساخت نمایش داده می‌شود).</param>
/// <param name="MobileKey">کلید نسخهٔ موبایل همان جایگاه (اسلایدر اصلی، بنر عریض و سربرگ دسته).</param>
/// <param name="Transparent">پس‌زمینهٔ شفاف (کارت تبلیغاتی: عکس کالا روی کارت رنگی سایت می‌نشیند).</param>
public sealed record DesignPreset(string Key, string Title, DesignKind Kind, int Width, int Height, BannerPosition? Position, string Hint,
    string? MobileKey = null, bool Transparent = false)
{
    /// <summary>نسبت ابعاد خوانا. مثال: ۱۹۲۰×۶۴۰ ← «۳:۱».</summary>
    public string Ratio
    {
        get
        {
            var gcd = Gcd(Width, Height);
            return $"{Width / gcd}:{Height / gcd}";
        }
    }

    /// <summary>طرح افقی پهن (عرض ≥ ۱٫۶ برابر ارتفاع) ← چیدمان «متن کنار تصویر»؛ وگرنه «متن بالای تصویر».</summary>
    public bool IsWide => Width >= Height * 1.6;

    private static int Gcd(int a, int b) => b == 0 ? Math.Max(1, a) : Gcd(b, a % b);
}

/// <summary>
/// اندازه‌های آمادهٔ استودیوی طراحی (دور ۲۱). هر جایگاه سایت اندازهٔ پیش‌فرض خودش را دارد تا مدیر لازم نباشد عدد بداند:
/// موقع «طرح تازه» یا تأیید ایده، اندازه از روی جایگاه انتخاب و در فرم نشان داده می‌شود.
/// <para>اندازه‌ها از نمایش واقعی صفحهٔ نخست (Pages/Index.cshtml) گرفته شده‌اند:
/// اسلایدر تمام‌عرض ۳:۱ (در موبایل ارتفاع ۲۴۰ و برش ← نسخهٔ موبایل جدا ۱۶:۱۰)، بنر عریض ۴:۱، کارت تبلیغاتی عکس شفاف کالا روی کارت رنگی،
/// کاور مقاله ۱۲۰۰×۶۳۰ (استاندارد Open Graph برای اشتراک در تلگرام/واتس‌اپ).</para>
/// <para>محل استفاده: DesignService، صفحه‌های /admin/design و ویرایشگر (design-studio.js اندازه را از سرور می‌گیرد).</para>
/// </summary>
public static class DesignPresets
{
    public const string Hero = "hero";
    public const string HeroMobile = "hero-mobile";
    public const string Wide = "wide";
    public const string WideMobile = "wide-mobile";
    public const string Promo = "promo";
    public const string PromoCard = "promo-card";
    public const string CategoryHeader = "category";
    public const string CategoryHeaderMobile = "category-mobile";
    public const string BlogCover = "blog-cover";
    public const string Post = "post";
    public const string Story = "story";
    public const string Free = "free";

    /// <summary>
    /// دور ۲۶ (خواستهٔ کاربر): بنرها پهن‌تر و کوتاه‌تر و <b>بدون نسخهٔ موبایل جدا</b> – یک نسخه برای همه؛ تیتر درشت (≥ ۲۰٪ ارتفاع)
    /// تا در موبایل هم خوانا باشد. اسلایدر ۱۹۲۰×۴۲۰، بنر عریض ۱۹۲۰×۳۰۰، بنر دوتایی ۹۶۰×۳۲۰، سربرگ دسته ۱۹۲۰×۳۰۰.
    /// اندازه‌های «-mobile» فقط برای باز شدن طرح‌های قدیمی مانده‌اند و در فرم‌ها نمایش داده نمی‌شوند (<see cref="IsLegacy"/>).
    /// «کارت تبلیغاتی شفاف» قدیمی (۸۰۰×۶۰۰) برای سازگاری با طرح‌های قبلی مانده است.
    /// </summary>
    public static readonly IReadOnlyList<DesignPreset> All =
    [
        new(Hero, "اسلایدر اصلی (بالای صفحهٔ نخست)", DesignKind.Banner, 1920, 420, BannerPosition.HeroSlider,
            "پهن و کوتاه؛ متن و دکمه سمت راست، تیتر درشت و کوتاه (در موبایل هم همین نسخه دیده می‌شود)."),
        new(HeroMobile, "اسلایدر اصلی – نسخهٔ موبایل (قدیمی)", DesignKind.Banner, 1080, 540, BannerPosition.HeroSlider,
            "فقط برای طرح‌های قدیمی؛ طرح تازه نسخهٔ موبایل جدا ندارد."),
        new(Wide, "بنر عریض (وسط صفحهٔ نخست)", DesignKind.Banner, 1920, 300, BannerPosition.WideBanner,
            "یک پیام کوتاه + دکمه؛ تیتر درشت تا در موبایل خوانا باشد."),
        new(WideMobile, "بنر عریض – نسخهٔ موبایل (قدیمی)", DesignKind.Banner, 1080, 360, BannerPosition.WideBanner,
            "فقط برای طرح‌های قدیمی."),
        new(Promo, "بنر دوتایی (کنار هم در صفحهٔ نخست)", DesignKind.Banner, 960, 320, BannerPosition.PromoCard,
            "دو بنر کنار هم (در موبایل زیر هم)؛ متن و دکمه روی خود تصویر."),
        new(CategoryHeader, "سربرگ صفحهٔ دسته", DesignKind.Banner, 1920, 300, BannerPosition.CategoryHeader,
            "بالای صفحهٔ دسته؛ نام دسته + یک پیام کوتاه."),
        new(CategoryHeaderMobile, "سربرگ دسته – نسخهٔ موبایل (قدیمی)", DesignKind.Banner, 1080, 360, BannerPosition.CategoryHeader,
            "فقط برای طرح‌های قدیمی."),
        new(PromoCard, "کارت تبلیغاتی قدیمی (عکس شفاف کالا)", DesignKind.Banner, 800, 600, BannerPosition.PromoCard,
            "پس‌زمینه شفاف است؛ عنوان، توضیح و دکمه از فیلدهای خود بنر روی کارت آبی سایت نوشته می‌شود.", Transparent: true),
        new(BlogCover, "کاور مقاله", DesignKind.BlogCover, 1200, 630, null,
            "تیتر کوتاه مقاله + عکس؛ همین تصویر در اشتراک‌گذاری و نتایج جست‌وجو دیده می‌شود."),
        new(Post, "پست مربعی شبکهٔ اجتماعی", DesignKind.Social, 1080, 1080, null, "برای اینستاگرام/تلگرام؛ دانلود و انتشار دستی."),
        new(Story, "استوری", DesignKind.Social, 1080, 1920, null, "بالا و پایین ۲۵۰ پیکسل را خالی بگذارید (زیر نوار برنامه می‌رود)."),
        new(Free, "اندازهٔ دلخواه", DesignKind.Free, 1200, 800, null, "عرض و ارتفاع را خودتان تعیین کنید (۲۰۰ تا ۴۰۰۰ پیکسل)."),
    ];

    /// <summary>دور ۲۶: اندازهٔ قدیمی نسخهٔ موبایل (در فرم‌ها نمایش داده نمی‌شود). مثال: «hero-mobile» ← true.</summary>
    public static bool IsLegacy(DesignPreset preset) => preset.Key.EndsWith("-mobile", StringComparison.Ordinal);

    /// <summary>پیدا کردن با کلید (کلید ناشناخته ← null). مثال: Find("wide").Width ← ۱۹۲۰.</summary>
    public static DesignPreset? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(p => string.Equals(p.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>اندازهٔ پیش‌فرض یک جایگاه بنر. مثال: WideBanner ← «wide»؛ None ← «hero».</summary>
    public static DesignPreset ForPosition(BannerPosition position) => position switch
    {
        BannerPosition.WideBanner => Find(Wide)!,
        BannerPosition.PromoCard => Find(Promo)!,
        BannerPosition.CategoryHeader => Find(CategoryHeader)!,
        _ => Find(Hero)!,
    };

    /// <summary>
    /// اندازهٔ دلخواه امن: هر ضلع بین ۲۰۰ و ۴۰۰۰ و مساحت حداکثر ۸ مگاپیکسل (حافظهٔ هاست ضعیف موقع ساخت تصویر).
    /// مثال: (5000، 100) ← (4000، 200)؛ (4000، 4000) ← ارتفاع کم می‌شود تا مساحت ≤ ۸ مگاپیکسل شود.
    /// </summary>
    public static (int Width, int Height) ClampSize(int width, int height)
    {
        width = Math.Clamp(width, 200, 4000);
        height = Math.Clamp(height, 200, 4000);
        const long maxArea = 8_000_000;
        if ((long)width * height > maxArea)
            height = (int)Math.Max(200, maxArea / width);
        return (width, height);
    }
}
