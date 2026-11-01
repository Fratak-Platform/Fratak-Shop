using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.SeoEngine;

// ═══════════════════════════════════════════════════════════════════════════
//  موتور سئوی هوشمند داخلی (مرحلهٔ هفتم – فاز ۸)
//  همه چیز داخل خود سیستم انجام می‌شود (بدون هیچ سرویس پولی یا هوش مصنوعی خارجی):
//   • SeoScore: آخرین نمرهٔ سئوی هر صفحه (محصول، دسته، برند، مقاله، صفحهٔ محتوا) و جزئیات بررسی‌ها.
//   • SeoSuggestion: پیشنهاد تولیدشده (عنوان، توضیحات متا، کلمهٔ کلیدی، نامک، متن، پرسش‌وپاسخ) که کارشناس سئو
//     تأیید یا رد می‌کند؛ رد کردن یعنی «دوباره بساز» و موتور نسخهٔ دیگری می‌نویسد.
//   • SeoResearchCache: نتیجهٔ جست‌وجوهای رایگان (پیشنهاد جست‌وجو، نتایج برتر، صفحه‌های رقبا) تا هر چیز
//     فقط یک بار در چند روز از اینترنت خوانده شود.
//   • SeoEngineRun: تاریخچهٔ اجرای خودکار (۲ تا ۳ بار در روز) و دستی.
//  موتور: Core/Services/Seo/Engine؛ زمان‌بندی و دسترسی به وب: Web/Infrastructure/Seo؛ پنل: /admin/seo/engine.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>نوع صفحه‌ای که موتور سئو رویش کار می‌کند. عددها در دیتابیس ذخیره می‌شوند؛ تغییر ندهید.</summary>
public enum SeoEntityKind : byte
{
    None = 0,
    Product = 1,
    Category = 2,
    Brand = 3,
    BlogPost = 4,
    ContentPage = 5,

    /// <summary>
    /// صفحه‌های ثابت سایت که موجودیت محتوایی ندارند (صفحهٔ اصلی، فهرست محصولات، وبلاگ، سؤالات متداول، تماس با ما،
    /// باشگاه مشتریان). مرحلهٔ شانزدهم. شناسهٔ هر کدام از عدد <c>SitePage</c> ساخته می‌شود و سئو در جدول PageSeo ذخیره می‌شود.
    /// </summary>
    StaticPage = 6,
}

/// <summary>
/// بخش‌هایی از صفحه که یک پیشنهاد تغییرشان می‌دهد (بیتی؛ چند بخش با هم).
/// مثال: Title | MetaDescription = پیشنهاد فقط عنوان و توضیحات متا را عوض می‌کند.
/// </summary>
[Flags]
public enum SeoFields
{
    None = 0,
    Title = 1,            // عنوان سئو (تگ title)
    MetaDescription = 2,  // توضیحات متا
    FocusKeyword = 4,     // کلمهٔ کلیدی اصلی
    Slug = 8,             // نامک آدرس (با ریدایرکت ۳۰۱ خودکار)
    Description = 16,     // متن توضیحات صفحه (فقط وقتی متن فعلی کم است)
    Faq = 32,             // پرسش‌وپاسخ صفحه + اسکیمای FAQPage
    ImageAlt = 64,        // متن جایگزین تصویرها
    All = Title | MetaDescription | FocusKeyword | Slug | Description | Faq | ImageAlt,
}

/// <summary>آخرین نمرهٔ سئوی یک صفحه. برای هر (نوع، شناسه) فقط یک ردیف.</summary>
public class SeoScore : BaseEntity
{
    public SeoEntityKind Kind { get; set; }

    public Guid EntityId { get; set; }

    /// <summary>نام صفحه هنگام بررسی (برای فهرست؛ مثلاً نام محصول).</summary>
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    /// <summary>آدرس عمومی صفحه. مثال: /product/گوشی-سامسونگ-a55</summary>
    [MaxLength(400)]
    public string Path { get; set; } = null!;

    /// <summary>نمرهٔ ۰ تا ۱۰۰.</summary>
    public int Score { get; set; }

    /// <summary>تعداد بررسی‌های «بد» (قرمز).</summary>
    public int Problems { get; set; }

    [MaxLength(200)]
    public string? FocusKeyword { get; set; }

    /// <summary>جزئیات بررسی‌ها به‌صورت JSON (فهرست SeoCheck در Core).</summary>
    public string ChecksJson { get; set; } = "[]";

    /// <summary>هش محتوای صفحه هنگام بررسی؛ اگر عوض نشده باشد بررسی دوباره لازم نیست.</summary>
    [MaxLength(64)]
    public string ContentHash { get; set; } = "";

    public DateTimeOffset AnalyzedAt { get; set; }

    /// <summary>تعداد بازدید صفحه هنگام بررسی (برای اولویت: صفحه‌های پربازدید زودتر بهینه می‌شوند).</summary>
    public int Views { get; set; }
}

/// <summary>وضعیت یک پیشنهاد سئو.</summary>
public enum SeoSuggestionStatus : byte
{
    None = 0,
    /// <summary>منتظر بررسی کارشناس سئو.</summary>
    Pending = 1,
    /// <summary>تأیید و روی صفحه اعمال شد.</summary>
    Applied = 2,
    /// <summary>رد شد؛ موتور در اجرای بعدی (یا فوراً) نسخهٔ دیگری می‌سازد.</summary>
    Rejected = 3,
    /// <summary>پیشنهاد تازه‌تری جایش را گرفت.</summary>
    Superseded = 4,
    /// <summary>صفحه بعد از ساخت پیشنهاد دستی ویرایش شده؛ پیشنهاد کهنه است و دوباره ساخته می‌شود.</summary>
    Stale = 5,
}

/// <summary>
/// یک پیشنهاد سئو برای یک صفحه. مقدار فعلی و پیشنهادی هر بخش، نمرهٔ قبل و بعد و دلیل‌ها ذخیره می‌شود تا
/// کارشناس فقط مقایسه و تأیید/رد کند.
/// </summary>
public class SeoSuggestion : BaseEntity
{
    public SeoEntityKind Kind { get; set; }

    public Guid EntityId { get; set; }

    [MaxLength(200)]
    public string EntityTitle { get; set; } = null!;

    [MaxLength(400)]
    public string Path { get; set; } = null!;

    public SeoSuggestionStatus Status { get; set; } = SeoSuggestionStatus.Pending;

    /// <summary>بخش‌هایی که این پیشنهاد تغییر می‌دهد.</summary>
    public SeoFields Fields { get; set; }

    public int ScoreBefore { get; set; }

    /// <summary>نمرهٔ پیش‌بینی‌شده بعد از اعمال همهٔ بخش‌ها (با همان تحلیل‌گر).</summary>
    public int ScoreAfter { get; set; }

    /// <summary>شمارهٔ نسخه؛ با هر رد کردن یکی بیشتر می‌شود تا متن متفاوتی ساخته شود.</summary>
    public int Variant { get; set; }

    /// <summary>مقدارهای پیشنهادی (JSON از SeoProposal در Core).</summary>
    public string ProposalJson { get; set; } = "{}";

    /// <summary>مقدارهای فعلی همان بخش‌ها هنگام ساخت پیشنهاد (برای نمایش مقایسه).</summary>
    public string CurrentJson { get; set; } = "{}";

    /// <summary>دلیل‌ها و کارهایی که فقط انسان می‌تواند انجام دهد (JSON آرایهٔ متن).</summary>
    public string ReasonsJson { get; set; } = "[]";

    /// <summary>خلاصهٔ تحقیق کلمه و رقبا که پیشنهاد بر اساسش ساخته شد (JSON).</summary>
    public string? ResearchJson { get; set; }

    /// <summary>هش محتوای صفحه هنگام ساخت؛ اگر قبل از تأیید عوض شود، پیشنهاد «کهنه» است.</summary>
    [MaxLength(64)]
    public string ContentHash { get; set; } = "";

    /// <summary>دلیل رد (برای نسخهٔ بعدی). مثال: «عنوان خیلی تبلیغاتی است».</summary>
    [MaxLength(500)]
    public string? RejectReason { get; set; }

    /// <summary>بخش‌هایی که کارشناس نپسندید (نسخهٔ بعد فقط همین‌ها را عوض می‌کند).</summary>
    public SeoFields RejectedFields { get; set; }

    /// <summary>بخش‌هایی که واقعاً اعمال شد (ممکن است کارشناس فقط بخشی را تأیید کند).</summary>
    public SeoFields AppliedFields { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    [MaxLength(150)]
    public string? ReviewedByName { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }
}

/// <summary>نوع دادهٔ ذخیره‌شده در کش تحقیق.</summary>
public enum SeoResearchKind : byte
{
    None = 0,
    /// <summary>پیشنهادهای جست‌وجو (autocomplete) برای یک عبارت.</summary>
    Suggest = 1,
    /// <summary>نتایج برتر جست‌وجو (آدرس و عنوان رقبا).</summary>
    Serp = 2,
    /// <summary>تحلیل یک صفحهٔ رقیب (عنوان، تیترها، تعداد کلمه، واژه‌های مهم).</summary>
    Page = 3,
    /// <summary>وضعیت یک لینک بیرونی (یابندهٔ لینک شکسته – فاز ۸ب). در جدول «منبع‌ها»ی موتور نمایش داده نمی‌شود.</summary>
    Link = 4,
}

/// <summary>
/// کش نتایج اینترنتی موتور سئو. کلید = عبارت نرمال‌شده یا آدرس. بعد از ExpiresAt دوباره خوانده می‌شود.
/// شکست‌ها هم (با Ok=false و انقضای کوتاه) ذخیره می‌شوند تا یک منبع قطع، مدام درخواست نخورد.
/// </summary>
public class SeoResearchCache : BaseEntity
{
    public SeoResearchKind Kind { get; set; }

    [MaxLength(450)]
    public string Key { get; set; } = null!;

    /// <summary>منبعی که جواب داد. مثال: bing، duckduckgo، google-suggest.</summary>
    [MaxLength(40)]
    public string? Source { get; set; }

    public string Json { get; set; } = "null";

    public bool Ok { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>چرا موتور اجرا شد.</summary>
public enum SeoRunTrigger : byte
{
    None = 0,
    Scheduled = 1,
    Manual = 2,
}

/// <summary>
/// یک اجرای موتور سئو. برای اجرای زمان‌بندی‌شده <see cref="SlotKey"/> یکتاست (مثلاً «1405-08-04#1») تا اگر چند
/// نمونهٔ سرور هم‌زمان باشند، هر نوبت فقط یک بار اجرا شود.
/// </summary>
public class SeoEngineRun : BaseEntity
{
    [MaxLength(40)]
    public string? SlotKey { get; set; }

    public SeoRunTrigger Trigger { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>تعداد صفحه‌های بررسی‌شده.</summary>
    public int Analyzed { get; set; }

    /// <summary>تعداد پیشنهادهای تازه.</summary>
    public int Suggested { get; set; }

    public int Failed { get; set; }

    /// <summary>خلاصهٔ اجرا یا خطا. مثال: «منبع نتایج جست‌وجو در دسترس نبود؛ پیشنهادها بدون تحلیل رقبا ساخته شد».</summary>
    [MaxLength(1000)]
    public string? Message { get; set; }
}
