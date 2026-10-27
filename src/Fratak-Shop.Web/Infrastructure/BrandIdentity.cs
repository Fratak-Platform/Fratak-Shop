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

    /// <summary>بخش اول پیش‌فرض متن برند (تنظیم BrandTextPrimary خالی).</summary>
    public const string DefaultTextPrimary = "فراتک";

    /// <summary>بخش دوم (رنگی) پیش‌فرض متن برند (تنظیم BrandTextAccent خالی).</summary>
    public const string DefaultTextAccent = "شاپ";

    /// <summary>زمینهٔ پیش‌فرض آیکون اپ (PWA) – مشکی، به خواست مدیر فروشگاه در مرحلهٔ دهم.</summary>
    public const string AppIconBackground = "#000000";

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

    /// <summary>
    /// آیکون‌های اپ برای مانیفست با purpose=any و اعلان‌ها: از مرحلهٔ دهم زمینهٔ مشکی با گوشهٔ گرد و نشان فراتک (F سفید + سبد آبی)،
    /// بدون هیچ متنی. منبع: Visual-Identities/Fratak-Shop/svg/app-icon-black.svg. مدیر می‌تواند در تنظیمات (PwaIconUrl) عوضش کند.
    /// </summary>
    public const string AppIcon192 = "/assets/brand/app-icon-192.png";

    /// <inheritdoc cref="AppIcon192"/>
    public const string AppIcon512 = "/assets/brand/app-icon-512.png";

    /// <summary>
    /// آیکون maskable (زمینهٔ مشکی تمام‌صفحه، نشان داخل ناحیهٔ امن ۸۰٪) تا اندروید آن را به هر شکلی برش بزند.
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
    public const string ManifestUrl = "/manifest.webmanifest?v=brand3";

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

/// <summary>
/// محل‌هایی که لوگو (آیکون + متن برند) نمایش داده می‌شود. برای هر محل در تنظیمات تعیین می‌شود که متن کنار آیکون باشد یا نه.
/// </summary>
public enum BrandPlace
{
    /// <summary>هدر سایت (دسکتاپ و منوی موبایل) – پیش‌فرض فقط آیکون.</summary>
    Header,

    /// <summary>صفحه‌های ورود و ثبت‌نام.</summary>
    Auth,

    /// <summary>سربرگ نوار کناری پنل مدیریت (زمینهٔ تیره).</summary>
    Admin,

    /// <summary>فاکتور مشتری و PDF آن.</summary>
    Invoice,
}

/// <summary>
/// برند آماده برای نمایش (از تنظیمات فروشگاه ساخته می‌شود؛ همهٔ آدرس‌ها و رنگ‌ها اعتبارسنجی شده‌اند).
/// مثال پیش‌فرض: آیکون mark.svg، متن «فراتک» + «شاپ»، رنگ متن null (سرمه‌ای/سفید با CSS)، رنگ تأکیدی #0069FD.
/// </summary>
/// <param name="IconUrl">آیکون زمینهٔ روشن.</param>
/// <param name="IconDarkUrl">آیکون زمینهٔ تیره (حالت شب سایت، نوار کناری پنل).</param>
/// <param name="IconRasterUrl">نسخهٔ قابل‌چاپ آیکون برای فاکتور PDF (html2canvas با SVG مشکل دارد).</param>
/// <param name="IsDefaultIcon">مدیر آیکون اختصاصی نگذاشته است.</param>
/// <param name="Primary">بخش اول متن. مثال: «فراتک».</param>
/// <param name="Accent">بخش رنگی متن یا null. مثال: «شاپ».</param>
/// <param name="TextColor">رنگ بخش اول روی زمینهٔ روشن یا null (= رنگ پیش‌فرض CSS).</param>
/// <param name="AccentColor">رنگ بخش رنگی (hex).</param>
public sealed record BrandLockup(
    string IconUrl,
    string IconDarkUrl,
    string IconRasterUrl,
    bool IsDefaultIcon,
    string Primary,
    string? Accent,
    string? TextColor,
    string AccentColor)
{
    /// <summary>متن کامل برای alt و aria-label. مثال: «فراتک شاپ».</summary>
    public string FullText => Accent is null ? Primary : Primary + " " + Accent;

    /// <summary>ساخت از تنظیمات با جایگزینی مقدارهای خالی/نامعتبر با پیش‌فرض برند.</summary>
    public static BrandLockup From(Fratak_Shop.Core.DTOs.Settings.StoreSettingDto settings)
    {
        var custom = Helpers.UrlSafety.IsSafeLink(settings.LogoUrl);
        var icon = custom ? settings.LogoUrl!.Trim() : BrandIdentity.Mark;
        var dark = Helpers.UrlSafety.IsSafeLink(settings.LogoDarkUrl) ? settings.LogoDarkUrl!.Trim() : custom ? icon : BrandIdentity.MarkOnDark;
        var primary = Clean(settings.BrandTextPrimary) ?? BrandIdentity.DefaultTextPrimary;
        var accentRaw = Clean(settings.BrandTextAccent);
        var accent = accentRaw is null ? BrandIdentity.DefaultTextAccent : accentRaw == "-" ? null : accentRaw;
        return new BrandLockup(icon, dark, custom ? icon : BrandIdentity.MarkPng, !custom, primary, accent,
            Hex(settings.BrandTextColor), Hex(settings.BrandAccentColor) ?? BrandIdentity.Blue);
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = value.Trim();
        return text.Length > 40 ? text[..40] : text;
    }

    /// <summary>فقط #RGB یا #RRGGBB؛ هر چیز دیگر (حتی تلاش برای تزریق CSS) null می‌شود.</summary>
    public static string? Hex(string? value) =>
        value is not null && System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$") ? value.Trim() : null;
}

/// <summary>
/// ورودی partial «_BrandLockup» (Pages/Shared/_BrandLockup.cshtml).
/// مثال: <c>@await Html.PartialAsync("_BrandLockup", new BrandLockupModel(BrandPlace.Auth, 44) { CssClass = "justify-center" })</c>
/// </summary>
/// <param name="Place">محل نمایش (تعیین می‌کند متن نوشته شود یا نه).</param>
/// <param name="IconHeight">ارتفاع آیکون به پیکسل؛ اندازهٔ متن متناسب با آن است (حدود ۰٫۶۵ ارتفاع).</param>
public sealed record BrandLockupModel(BrandPlace Place, int IconHeight)
{
    /// <summary>کلاس‌های اضافهٔ ظرف (چینش و فاصله).</summary>
    public string? CssClass { get; init; }

    /// <summary>روی زمینهٔ همیشه تیره (پنل): فقط آیکون تیره و متن سفید.</summary>
    public bool OnDark { get; init; }

    /// <summary>نمایش متن بدون توجه به تنظیم محل (پیش‌نمایش صفحهٔ تنظیمات).</summary>
    public bool ForceText { get; init; }

    /// <summary>آیکون نسخهٔ PNG/قابل‌چاپ (فاکتور PDF).</summary>
    public bool Raster { get; init; }

    /// <summary>
    /// عرض تقریبی آیکون پیش‌فرض برای رزرو جا (جلوگیری از پرش صفحه). نسبت نشان برند ۲۴۰٫۹ × ۱۹۲٫۹ است.
    /// مثال: ارتفاع ۴۰ → عرض ۵۰.
    /// </summary>
    public int IconWidth => (int)Math.Round(IconHeight * MarkAspectRatio);

    /// <summary>نسبت عرض به ارتفاع نشان برند (viewBox فایل mark.svg).</summary>
    public const double MarkAspectRatio = 240.9 / 192.9;

    /// <summary>اندازهٔ قلم متن برند به پیکسل. مثال: آیکون ۴۰ → قلم ۲۶.</summary>
    public int FontSize => Math.Max(12, (int)Math.Round(IconHeight * 0.65));
}
