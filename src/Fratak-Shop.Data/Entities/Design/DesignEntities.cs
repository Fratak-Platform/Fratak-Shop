using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Design;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۱ – «استودیوی طراحی» (بنر، کاور مقاله، پست شبکهٔ اجتماعی) و «صفحه‌ساز کمپین»
//  بدون هیچ سرویس هوش مصنوعی یا پولی: طرح‌ها به شکل JSON ویرایشگر Fabric.js ذخیره می‌شوند تا همیشه دوباره قابل ویرایش باشند
//  و خروجی نهایی یک تصویر WebP سبک است (سایت فقط عکس آماده را نشان می‌دهد؛ هیچ پردازشی سمت مشتری نیست).
//  کجا استفاده می‌شوند؟
//   • DesignDoc    ← Core/Services/Design/DesignService.cs (ایده‌ها، ذخیره، تأیید و انتشار) و صفحه‌های پنل /admin/design
//   • LandingPage  ← Core/Services/Design/LandingPageService.cs، صفحه‌ساز پنل /admin/campaigns و صفحهٔ عمومی /campaign/{slug}
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// دور ۲۶ – حرکت بنر («گیف»): None = ثابت، Soft = یک بار ورود نرم عنصرها، Loop = ورود/خروج پیوسته (چرخهٔ ۹ ثانیه‌ای).
/// برای کاربرانی که «کاهش حرکت» را در سیستم‌شان روشن کرده‌اند همیشه ثابت نشان داده می‌شود.
/// </summary>
public enum BannerMotion : byte
{
    None = 0,
    Soft = 1,
    Loop = 2,
}

/// <summary>نوع طرح؛ اندازهٔ پیش‌فرض از روی نوع و «جایگاه» انتخاب می‌شود (DesignPresets).</summary>
public enum DesignKind : byte
{
    None = 0,

    /// <summary>بنر صفحهٔ اصلی (اسلایدر، بنر عریض، کارت تبلیغاتی).</summary>
    Banner = 1,

    /// <summary>کاور (تصویر شاخص) مقالهٔ بلاگ – ۱۲۰۰×۶۳۰ استاندارد اشتراک‌گذاری.</summary>
    BlogCover = 2,

    /// <summary>پست/استوری شبکهٔ اجتماعی (دانلود برای انتشار دستی).</summary>
    Social = 3,

    /// <summary>طرح آزاد با اندازهٔ دلخواه.</summary>
    Free = 4,
}

/// <summary>وضعیت طرح. مثال: ایدهٔ سامانه ← Suggested؛ مدیر «تأیید و انتشار» زد ← Published.</summary>
public enum DesignStatus : byte
{
    /// <summary>پیش‌نویس (ساخت یا ویرایش دستی، هنوز منتشر نشده).</summary>
    Draft = 0,

    /// <summary>ایدهٔ سامانه که منتظر تأیید مدیر است.</summary>
    Suggested = 1,

    /// <summary>منتشر شده (به بنر یا مقاله وصل است).</summary>
    Published = 2,

    /// <summary>رد شده (همان ایده دیگر پیشنهاد نمی‌شود).</summary>
    Rejected = 3,
}

/// <summary>
/// یک طرح گرافیکی قابل ویرایش. Json = خروجی <c>canvas.toObject()</c> ویرایشگر Fabric.js (لایه‌ها، متن‌ها، رنگ‌ها، تصویرها).
/// ImageName = آخرین خروجی WebP (پوشهٔ uploads/banners یا uploads/blogs بسته به نوع).
/// </summary>
public class DesignDoc : BaseEntity
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    public DesignKind Kind { get; set; }

    public DesignStatus Status { get; set; }

    /// <summary>ساختهٔ سامانه (ایدهٔ هوشمند) یا مدیر.</summary>
    public bool IsSmart { get; set; }

    /// <summary>کلید اندازهٔ آماده (hero، hero-mobile، wide، promo، blog-cover، post، story، free).</summary>
    [Required]
    [MaxLength(40)]
    public string PresetKey { get; set; } = "free";

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>JSON ویرایشگر (حداکثر ۱ مگابایت؛ تصویرها فقط با آدرس محلی، نه base64).</summary>
    [Required]
    public string Json { get; set; } = "{}";

    /// <summary>نام فایل خروجی WebP.</summary>
    [MaxLength(200)]
    public string? ImageName { get; set; }

    /// <summary>
    /// نسخهٔ موبایل همان طرح (فقط اسلایدر اصلی: ۹۶۰×۶۰۰). در موبایل اسلایدر کوتاه است و نسخهٔ دسکتاپ بریده می‌شود؛
    /// برای همین سامانه هر ایدهٔ اسلایدر را در دو قاب می‌سازد و ویرایشگر هر دو را با زبانه نشان می‌دهد.
    /// </summary>
    public string? MobileJson { get; set; }

    /// <summary>دور ۲۶ – حرکت بنر در سایت (ثابت / ورود نرم / ورود و خروج پیوسته).</summary>
    public BannerMotion Animation { get; set; }

    [MaxLength(200)]
    public string? MobileImageName { get; set; }

    /// <summary>کلید یکتای ایده (برای تکراری نشدن). مثال: «deal:3f2c…:1405-08».</summary>
    [MaxLength(120)]
    public string? IdeaKey { get; set; }

    /// <summary>چرا این ایده؟ مثال: «۲۵٪ تخفیف و ۱۲ عدد موجودی؛ پربازدید هفته».</summary>
    [MaxLength(500)]
    public string? IdeaNote { get; set; }

    /// <summary>تیتر اصلی طرح (عنوان و متن جایگزین بنر).</summary>
    [MaxLength(300)]
    public string? Headline { get; set; }

    [MaxLength(300)]
    public string? Subline { get; set; }

    [MaxLength(60)]
    public string? ButtonText { get; set; }

    /// <summary>لینک مقصد بنر (فقط آدرس داخلی یا https).</summary>
    [MaxLength(500)]
    public string? LinkUrl { get; set; }

    /// <summary>بنر ساخته‌شده از این طرح (بعد از انتشار).</summary>
    public Guid? BannerId { get; set; }

    /// <summary>مقاله‌ای که این طرح کاور آن است.</summary>
    public Guid? BlogPostId { get; set; }

    /// <summary>پایان اعتبار ایده (مثلاً پایان جشنواره)؛ بعدش در فهرست پیشنهادها نمی‌آید.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>دور ۲۴: دستهٔ ایده (مثلاً «حراج هدفون»)؛ بنر «سربرگ صفحهٔ دسته» با همین دسته منتشر می‌شود.</summary>
    public Guid? CategoryId { get; set; }

    [MaxLength(150)]
    public string? UpdatedByName { get; set; }
}

/// <summary>
/// صفحهٔ کمپین ساخته‌شده با صفحه‌ساز کشیدنی (GrapesJS). Html/Css پاک‌سازی‌شده‌اند و CSS فقط داخل «.fs-lp» اثر دارد
/// (هدر/فوتر سایت خراب نمی‌شود). ProjectJson = دادهٔ کامل ویرایشگر برای ویرایش دوباره.
/// <para>آدرس عمومی: /campaign/{Slug} (نامک انگلیسی). در منوی بالای سایت لینک «صفحات» ساخته نمی‌شود.</para>
/// </summary>
public class LandingPage : BaseEntity
{
    [Required]
    [MaxLength(200)]
    public string Slug { get; set; } = null!;

    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = null!;

    [MaxLength(300)]
    public string? MetaTitle { get; set; }

    [MaxLength(400)]
    public string? MetaDescription { get; set; }

    /// <summary>تصویر اشتراک‌گذاری (og:image) – آدرس محلی.</summary>
    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    /// <summary>دادهٔ کامل ویرایشگر (فقط پنل).</summary>
    public string? ProjectJson { get; set; }

    /// <summary>HTML پاک‌سازی‌شدهٔ صفحه (نمایش عمومی).</summary>
    public string? Html { get; set; }

    /// <summary>CSS محدودشده به «.fs-lp» (نمایش عمومی).</summary>
    public string? Css { get; set; }

    public bool ShowHeader { get; set; } = true;

    public bool ShowFooter { get; set; } = true;

    public bool IsActive { get; set; }

    /// <summary>شروع/پایان نمایش (خالی = همیشه). بعد از پایان صفحه ۴۰۴ نمی‌دهد؛ پیام «کمپین تمام شد» با لینک فروشگاه.</summary>
    public DateTimeOffset? StartAt { get; set; }

    public DateTimeOffset? EndAt { get; set; }

    /// <summary>قالب شروع (campaign، product، festival، blank).</summary>
    [MaxLength(40)]
    public string? TemplateKey { get; set; }

    /// <summary>
    /// دور ۲۹ (#۱۱): «حالت کد» به جای صفحه‌ساز. روشن ← صفحهٔ عمومی کد خام HTML/CSS/JS زیر را داخل یک iframe ایزوله
    /// (sandbox بدون دسترسی به کوکی و حساب کاربر، با CSP جدا) نشان می‌دهد؛ خروجی صفحه‌ساز دست نمی‌خورد و با خاموش کردن برمی‌گردد.
    /// </summary>
    public bool CodeMode { get; set; }

    /// <summary>دور ۲۹ (#۱۱): HTML خام حالت کد (پاک‌سازی نمی‌شود؛ فقط داخل iframe ایزوله اجرا می‌شود).</summary>
    public string? CodeHtml { get; set; }

    /// <summary>دور ۲۹ (#۱۱): CSS خام حالت کد.</summary>
    public string? CodeCss { get; set; }

    /// <summary>دور ۲۹ (#۱۱): جاوااسکریپت خام حالت کد.</summary>
    public string? CodeJs { get; set; }

    [MaxLength(150)]
    public string? UpdatedByName { get; set; }
}
