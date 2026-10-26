namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// هویت بصری فراتک شاپ – تنها منبع مسیر فایل‌ها و رنگ‌های برند در کد سرور.
/// <para>
/// منبع اصلی طراحی: پوشهٔ <c>Visual-Identities/Fratak-Shop</c> در ریشهٔ مخزن (tokens.css، svg، png، فونت‌ها).
/// نسخهٔ وب همان فایل‌ها در <c>wwwroot/assets/brand</c> و <c>wwwroot/assets/font</c> کپی شده است
/// (راهنما: <c>docs/BRAND.md</c>).
/// </para>
/// <para>
/// محل استفاده: <see cref="SiteContext"/> (لوگو و فاوآیکن پیش‌فرض)، <see cref="PwaEndpoints"/> (آیکون‌ها و رنگ
/// مانیفست)، صفحهٔ تعمیرات (<c>OpsMiddleware</c>)، تصویر پیش‌فرض اشتراک‌گذاری (<c>PageMetaService</c>) و
/// تست <c>BrandAssetsTests</c> که وجود همهٔ این فایل‌ها را بررسی می‌کند.
/// اگر فایلی به این کلاس اضافه شد، همان را در wwwroot هم بگذارید وگرنه تست شکست می‌خورد.
/// </para>
/// </summary>
public static class BrandIdentity
{
    // ── رنگ‌ها (Visual-Identities/Fratak-Shop/tokens.css) ─────────────────────────────

    /// <summary>سرمه‌ای برند (رنگ «جوهر»): متن اصلی، نوار کناری پنل، رنگ نوار مرورگر.</summary>
    public const string Navy = "#0B1927";

    /// <summary>آبی اصلی برند: دکمه‌ها، لینک‌ها و بخش «Shop» لوگو.</summary>
    public const string Blue = "#0069FD";

    /// <summary>دو سر گرادیان آیکون اپ (۱۳۵ درجه).</summary>
    public const string GradientStart = "#019BFF";

    /// <inheritdoc cref="GradientStart"/>
    public const string GradientEnd = "#0156FE";

    /// <summary>پس‌زمینهٔ روشن («کاغذ») برند.</summary>
    public const string Paper = "#FDFDFD";

    /// <summary>
    /// رنگ پیش‌فرض نوار مرورگر و مانیفست PWA – مثل site.webmanifest هویت بصری، سرمه‌ای.
    /// مدیر می‌تواند در تنظیمات (ThemeColor) عوضش کند.
    /// </summary>
    public const string DefaultThemeColor = Navy;

    /// <summary>رنگ پیش‌فرض نوار اطلاعیهٔ بالای سایت (AnnouncementColor خالی یا نامعتبر).</summary>
    public const string DefaultAnnouncementColor = Navy;

    // ── فایل‌ها (wwwroot/assets/brand) ───────────────────────────────────────────────

    /// <summary>لوگوی افقی (نشان + «Fratak Shop») برای زمینهٔ روشن.</summary>
    public const string Logo = "/assets/brand/logo.svg";

    /// <summary>لوگوی افقی برای زمینهٔ تیره (نوشتهٔ سفید، پس‌زمینهٔ شفاف) – حالت تیرهٔ فروشگاه.</summary>
    public const string LogoOnDark = "/assets/brand/logo-on-dark.svg";

    /// <summary>
    /// نسخهٔ PNG لوگو؛ برای جاهایی که SVG مناسب نیست: schema.org (Organization.logo)، فاکتور PDF
    /// (html2canvas) و ایمیل.
    /// </summary>
    public const string LogoPng = "/assets/brand/logo-1024.png";

    /// <summary>نشان تنها («F» با سبد خرید) برای زمینهٔ روشن – آواتار نویسندهٔ بلاگ و ...</summary>
    public const string Mark = "/assets/brand/mark.svg";

    /// <summary>نسخهٔ PNG نشان (۵۱۲ پیکسل، پس‌زمینهٔ شفاف) – جای خالی لوگوی برندهای کالا (ImageHelper.BrandPlaceholder).</summary>
    public const string MarkPng = "/assets/brand/mark-512.png";

    /// <summary>نشان تنها برای زمینهٔ تیره (F سفید + سبد آبی) – سربرگ نوار کناری پنل مدیریت.</summary>
    public const string MarkOnDark = "/assets/brand/mark-on-dark.svg";

    /// <summary>فاوآیکن برداری (مرورگرهای جدید).</summary>
    public const string FaviconSvg = "/assets/brand/favicon.svg";

    /// <summary>فاوآیکن ICO (۱۶/۳۲/۴۸) در ریشهٔ سایت؛ مرورگرها و ربات‌ها خودکار /favicon.ico را می‌خوانند.</summary>
    public const string FaviconIco = "/favicon.ico";

    /// <summary>آیکون صفحهٔ اصلی iOS (۱۸۰×۱۸۰).</summary>
    public const string AppleTouchIcon = "/assets/brand/apple-touch-icon.png";

    /// <summary>آیکون‌های اپ (گرادیان برند با گوشهٔ گرد) برای مانیفست با purpose=any و اعلان‌ها.</summary>
    public const string AppIcon192 = "/assets/brand/app-icon-192.png";

    /// <inheritdoc cref="AppIcon192"/>
    public const string AppIcon512 = "/assets/brand/app-icon-512.png";

    /// <summary>
    /// آیکون maskable (گرادیان تمام‌صفحه، نشان سفید داخل ناحیهٔ امن ۸۰٪) تا اندروید آن را به هر شکلی برش بزند.
    /// </summary>
    public const string AppIconMaskable192 = "/assets/brand/app-icon-maskable-192.png";

    /// <inheritdoc cref="AppIconMaskable192"/>
    public const string AppIconMaskable512 = "/assets/brand/app-icon-maskable-512.png";

    /// <summary>نشان تک‌رنگ سفید با پس‌زمینهٔ شفاف برای «badge» اعلان‌های وب پوش (نوار وضعیت اندروید).</summary>
    public const string NotificationBadge = "/assets/brand/notification-badge-96.png";

    /// <summary>تصویر پیش‌فرض اشتراک‌گذاری (Open Graph، ۱۲۰۰×۶۳۰) وقتی صفحه تصویر خودش را ندارد.</summary>
    public const string OgImage = "/assets/brand/og-default.png";

    /// <summary>
    /// آدرس مانیفست PWA با شمارهٔ نسخه. با هر تغییر آیکون‌ها/نام اپ این عدد را بالا ببرید تا مرورگرها و CDN ها
    /// مانیفست قدیمی (و آیکون قدیمی) را از کش نخوانند. شناسهٔ اپ («id» داخل مانیفست) ثابت است، پس اپ نصب‌شده
    /// همان اپ می‌ماند و فقط آیکونش به‌روز می‌شود.
    /// محل استفاده: _Layout (تگ link rel=manifest).
    /// </summary>
    public const string ManifestUrl = "/manifest.webmanifest?v=brand2";

    /// <summary>
    /// آدرس‌های آیکون/لوگوی قالب قدیمی که مرورگرها، اپ‌های نصب‌شده، کش CDN یا مانیفست‌های کهنه ممکن است هنوز
    /// درخواست کنند. به جای 404 همان فایل برند جدید برگردانده می‌شود (PwaEndpoints) تا آیکون قدیمی هیچ‌جا نماند.
    /// مثال: مانیفست کهنه‌ای که در کش گوشی مانده «/assets/image/pwa/icon-192.png» را می‌خواهد ← app-icon-192.png.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LegacyIcons { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["/assets/image/pwa/icon-192.png"] = AppIcon192,
        ["/assets/image/pwa/icon-512.png"] = AppIcon512,
        ["/assets/image/pwa/maskable-512.png"] = AppIconMaskable512,
        ["/assets/image/pwa/apple-touch-icon.png"] = AppleTouchIcon,
        ["/assets/image/logo.png"] = LogoPng,
        // سافاری iOS وقتی تگ apple-touch-icon را پیدا نکند (یا صفحه هنوز بار نشده) این دو آدرس ریشه را امتحان می‌کند.
        ["/apple-touch-icon.png"] = AppleTouchIcon,
        ["/apple-touch-icon-precomposed.png"] = AppleTouchIcon,
    };

    /// <summary>همهٔ فایل‌های بالا (برای تست وجود فایل‌ها).</summary>
    public static IReadOnlyList<string> AllAssets { get; } =
    [
        Logo, LogoOnDark, LogoPng, Mark, MarkPng, MarkOnDark, FaviconSvg, FaviconIco, AppleTouchIcon,
        AppIcon192, AppIcon512, AppIconMaskable192, AppIconMaskable512, NotificationBadge, OgImage,
    ];
}
