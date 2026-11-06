using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.SeoEngine;

// ═══════════════════════════════════════════════════════════════════════════
//  جدول‌های «سئوی خودکار» (دور سئوی ۲۱۰ موردی)
//   • SeoAutopilotConfig : تنظیمات (یک ردیف): حالت هر بخش، کلید IndexNow، کلید Bing، وضعیت کارهای یک‌باره.
//   • SeoAutoState       : وضعیت هر صفحه: متن‌هایی که خودِ سیستم نوشته (تا اگر انسان عوضشان کرد، دست نخورند)،
//                          زمان آخرین بازنویسی (فاصلهٔ ۲۱ روزه) و هش HTML مرتب‌شده.
//   • SeoRevision        : تاریخچهٔ هر تغییر خودکار با «قبل/بعد» و امکان برگرداندن.
//   • SeoCrawlRun/Page   : خزش شبانهٔ خود سایت (مثل ربات گوگل) و نتیجهٔ هر صفحه.
//   • SeoTask            : فهرست کارهای سئو با اولویت (خودکار حل‌شدنی یا نیازمند انسان).
//   • SeoBotVisit        : بازدید ربات‌های موتور جست‌وجو (تجمیع روزانه) + تشخیص ربات جعلی.
//   • SeoVitalStat       : سرعت واقعی کاربران (Core Web Vitals) به تفکیک نوع صفحه و دستگاه.
//   • SeoTitleTest       : آزمایش عنوان (نسخهٔ تازه برای مدتی و مقایسه با نرخ کلیک قبلی).
//  پیکربندی ایندکس‌ها: Configurations/SeoAutopilotConfiguration.cs
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// تنظیمات سئوی خودکار (فقط یک ردیف). تنظیمات ریز (حالت هر بخش، سقف‌ها) به‌صورت JSON در <see cref="OptionsJson"/> است
/// تا افزودن گزینهٔ تازه به مهاجرت پایگاه داده نیاز نداشته باشد (مدل: Core/Services/Seo/Autopilot/SeoAutopilotOptions.cs).
/// </summary>
public class SeoAutopilotConfig : BaseEntity
{
    /// <summary>تنظیمات بخش‌ها به JSON. خالی ← پیش‌فرض‌ها (همه روشن و ایمن).</summary>
    public string? OptionsJson { get; set; }

    /// <summary>کلید IndexNow (۳۲ نویسهٔ هگز)؛ فایل /{key}.txt همین را برمی‌گرداند. اولین اجرا خودکار ساخته می‌شود.</summary>
    [MaxLength(64)]
    public string? IndexNowKey { get; set; }

    /// <summary>کلید API ابزار وب‌مستر بینگ (رایگان)، رمزنگاری‌شده. برای بک‌لینک‌ها و آمار جست‌وجوی بینگ.</summary>
    public string? BingApiKeyProtected { get; set; }

    public DateTimeOffset? LastNightlyRunAt { get; set; }

    /// <summary>خلاصهٔ آخرین اجرای شبانه. مثال: «۳۲۰ صفحه خزش شد، ۱۴ فیلد پر شد، ۲ ریدایرکت ساخته شد».</summary>
    [MaxLength(4000)]
    public string? LastNightlySummary { get; set; }

    public DateTimeOffset? LastWeeklyReportAt { get; set; }

    /// <summary>کار یک‌بارهٔ «تبدیل نامک‌های فارسی به انگلیسی» تمام شد (دوباره هم بی‌خطر است).</summary>
    public DateTimeOffset? SlugMigrationDoneAt { get; set; }

    /// <summary>کار یک‌بارهٔ «تبدیل همهٔ تصویرهای JPG/PNG به WebP» تمام شد.</summary>
    public DateTimeOffset? ImageMigrationDoneAt { get; set; }

    /// <summary>آخرین آمار بک‌لینک‌ها از بینگ (JSON: [{url, count}]).</summary>
    public string? BacklinksJson { get; set; }

    public DateTimeOffset? BacklinksFetchedAt { get; set; }

    /// <summary>وضعیت گواهی SSL دامنه. مثال: «معتبر تا ۱۴۰۵/۰۳/۱۲ (۸۴ روز)».</summary>
    [MaxLength(500)]
    public string? SslStatus { get; set; }

    public DateTimeOffset? SslExpiresAt { get; set; }
}

/// <summary>وضعیت سئوی خودکار یک صفحه (برای هر (نوع، شناسه) یک ردیف).</summary>
public class SeoAutoState : BaseEntity
{
    public SeoEntityKind Kind { get; set; }

    public Guid EntityId { get; set; }

    /// <summary>
    /// «مهر» متن‌هایی که خود سیستم نوشته: JSON {"field": "hash"}. اگر مقدار فعلی یک فیلد با مهرش برابر باشد، یعنی هنوز
    /// متن خودکار است و می‌تواند با دادهٔ تازه (قیمت، موجودی) به‌روز شود؛ اگر انسان عوضش کرده باشد، دیگر دست نمی‌خورد.
    /// </summary>
    public string? StampsJson { get; set; }

    public DateTimeOffset? LastFilledAt { get; set; }

    public DateTimeOffset? LastHtmlFixAt { get; set; }

    /// <summary>آخرین بازنویسی دادهٔ‌محور عنوان/توضیحات (فاصلهٔ حداقل ۲۱ روزه، ضد جریمه).</summary>
    public DateTimeOffset? LastRewriteAt { get; set; }

    /// <summary>هش HTML بعد از آخرین مرتب‌سازی (اگر متن عوض نشده باشد، دوباره پردازش نمی‌شود).</summary>
    [MaxLength(64)]
    public string? LastHtmlHash { get; set; }
}

/// <summary>منبع یک تغییر خودکار.</summary>
public enum SeoRevisionSource : byte
{
    None = 0,
    AutoFill = 1,
    HtmlFix = 2,
    Rewrite = 3,
    TitleTest = 4,
    Rollback = 5,
    Slug = 6,
    Image = 7,
    Manual = 8,

    /// <summary>«توضیحات محصول» حرفه‌ای که دستیار هوشمند نوشت (دور بیستم). هش متن بعد از این تغییر = «نوشتهٔ دستیار».</summary>
    SmartReview = 9,
}

/// <summary>
/// تاریخچهٔ تغییرهای سئوی خودکار: «قبل» و «بعد» هر فیلد به JSON (مثال: {"Title":"…","MetaDescription":"…"}).
/// صفحهٔ /admin/seo/history آن را با امکان «برگرداندن» نشان می‌دهد.
/// </summary>
public class SeoRevision : BaseEntity
{
    public SeoEntityKind Kind { get; set; }

    public Guid EntityId { get; set; }

    [Required]
    [MaxLength(500)]
    public string Path { get; set; } = null!;

    [Required]
    [MaxLength(300)]
    public string PageTitle { get; set; } = null!;

    public SeoRevisionSource Source { get; set; }

    /// <summary>خلاصهٔ فارسی تغییر. مثال: «توضیحات متا و ۳ متن جایگزین تصویر پر شد».</summary>
    [Required]
    [MaxLength(1000)]
    public string Summary { get; set; } = null!;

    [Required]
    public string BeforeJson { get; set; } = null!;

    [Required]
    public string AfterJson { get; set; } = null!;

    [MaxLength(150)]
    public string? ByName { get; set; }

    public DateTimeOffset? RevertedAt { get; set; }

    [MaxLength(150)]
    public string? RevertedBy { get; set; }
}

/// <summary>یک اجرای خزش داخلی سایت.</summary>
public class SeoCrawlRun : BaseEntity
{
    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int Pages { get; set; }

    public int Errors { get; set; }

    public int Warnings { get; set; }

    /// <summary>نمرهٔ سلامت سئوی کل سایت (۰ تا ۱۰۰).</summary>
    public int HealthScore { get; set; }

    /// <summary>آمار کامل اجرا به JSON (شمارش هر نوع مشکل، میانگین زمان پاسخ و ...).</summary>
    public string? SummaryJson { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }
}

/// <summary>نتیجهٔ خزش یک صفحه.</summary>
public class SeoCrawlPage : BaseEntity
{
    public Guid RunId { get; set; }

    [Required]
    [MaxLength(500)]
    public string Path { get; set; } = null!;

    public int Status { get; set; }

    /// <summary>فاصلهٔ کلیک از صفحهٔ اصلی (۰ = خود صفحهٔ اصلی).</summary>
    public int Depth { get; set; }

    public int Ms { get; set; }

    public int Bytes { get; set; }

    [MaxLength(500)]
    public string? Title { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? H1 { get; set; }

    public int H1Count { get; set; }

    public int Words { get; set; }

    public int Images { get; set; }

    public int ImagesWithoutAlt { get; set; }

    public int InLinks { get; set; }

    public int OutLinks { get; set; }

    public int ExternalLinks { get; set; }

    [MaxLength(500)]
    public string? Canonical { get; set; }

    public bool NoIndex { get; set; }

    public bool InSitemap { get; set; }

    [MaxLength(500)]
    public string? SchemaTypes { get; set; }

    public int SchemaErrors { get; set; }

    /// <summary>اثر انگشت متن (SimHash) برای پیدا کردن صفحه‌های تقریباً تکراری.</summary>
    public long SimHash { get; set; }

    /// <summary>کد مشکل‌های صفحه به JSON. مثال: ["missing-meta","multiple-h1"].</summary>
    public string? IssuesJson { get; set; }
}

public enum SeoTaskSeverity : byte
{
    None = 0,
    Info = 1,
    Warning = 2,
    Critical = 3,
}

public enum SeoTaskStatus : byte
{
    None = 0,
    Open = 1,
    Fixed = 2,
    Dismissed = 3,
    AutoFixed = 4,
}

/// <summary>
/// یک کار سئو (مثلاً «۳ صفحه توضیحات متای تکراری دارند»). هر کار یک اثر انگشت یکتا دارد تا تکرار نشود؛
/// اگر مشکل در خزش بعدی دیده نشود، خودکار «حل‌شده» می‌شود.
/// </summary>
public class SeoTask : BaseEntity
{
    [Required]
    [MaxLength(60)]
    public string Code { get; set; } = null!;

    public SeoTaskSeverity Severity { get; set; }

    public SeoTaskStatus Status { get; set; }

    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = null!;

    [Required]
    [MaxLength(2000)]
    public string Detail { get; set; } = null!;

    [MaxLength(500)]
    public string? Path { get; set; }

    public SeoEntityKind Kind { get; set; }

    public Guid? EntityId { get; set; }

    /// <summary>اثر تخمینی روی سئو (۱ تا ۱۰۰)؛ مرتب‌سازی فهرست کارها.</summary>
    public int Impact { get; set; }

    /// <summary>سیستم می‌تواند خودش حلش کند (دکمهٔ «اصلاح خودکار»).</summary>
    public bool AutoFixable { get; set; }

    [Required]
    [MaxLength(64)]
    public string Fingerprint { get; set; } = null!;

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    [MaxLength(150)]
    public string? ResolvedBy { get; set; }
}

/// <summary>بازدید ربات‌های موتور جست‌وجو؛ برای هر (روز، ربات، مسیر، کد پاسخ) یک ردیف.</summary>
public class SeoBotVisit : BaseEntity
{
    public DateOnly Date { get; set; }

    /// <summary>نام ربات. مثال: Googlebot، Bingbot، YandexBot.</summary>
    [Required]
    [MaxLength(40)]
    public string Bot { get; set; } = null!;

    [Required]
    [MaxLength(300)]
    public string Path { get; set; } = null!;

    public int Status { get; set; }

    public int Hits { get; set; }

    /// <summary>بازدیدهایی که با DNS معکوس تأیید شدند (واقعاً از گوگل/بینگ بودند).</summary>
    public int VerifiedHits { get; set; }

    /// <summary>بازدیدهایی که خود را ربات گوگل/بینگ معرفی کردند ولی نبودند (ربات جعلی).</summary>
    public int FakeHits { get; set; }

    public long TotalMs { get; set; }

    public DateTimeOffset LastAt { get; set; }
}

/// <summary>سرعت واقعی کاربران (صدک ۷۵) برای هر (روز، نوع صفحه، دستگاه، شاخص).</summary>
public class SeoVitalStat : BaseEntity
{
    public DateOnly Date { get; set; }

    /// <summary>نوع صفحه. مثال: product، category، home، blog.</summary>
    [Required]
    [MaxLength(40)]
    public string PageType { get; set; } = null!;

    /// <summary>mobile یا desktop.</summary>
    [Required]
    [MaxLength(10)]
    public string Device { get; set; } = null!;

    /// <summary>LCP، CLS، INP، FCP یا TTFB.</summary>
    [Required]
    [MaxLength(10)]
    public string Metric { get; set; } = null!;

    public double P75 { get; set; }

    public int Samples { get; set; }

    public int Good { get; set; }

    public int Poor { get; set; }

    /// <summary>نسخهٔ برنامه هنگام اندازه‌گیری (برای تشخیص کند شدن پس از استقرار).</summary>
    [MaxLength(40)]
    public string? AppVersion { get; set; }
}

public enum SeoTitleTestStatus : byte
{
    None = 0,
    Running = 1,
    Won = 2,
    Lost = 3,
    Inconclusive = 4,
    Cancelled = 5,
}

/// <summary>
/// آزمایش عنوان: عنوان تازه برای مدتی روی صفحه می‌ماند و نرخ کلیک (نسبت به جایگاه) با دورهٔ قبل مقایسه می‌شود؛
/// اگر بهتر نبود، عنوان قبلی خودکار برمی‌گردد.
/// </summary>
public class SeoTitleTest : BaseEntity
{
    public SeoEntityKind Kind { get; set; }

    public Guid EntityId { get; set; }

    [Required]
    [MaxLength(500)]
    public string Path { get; set; } = null!;

    [Required]
    [MaxLength(300)]
    public string OldTitle { get; set; } = null!;

    [Required]
    [MaxLength(300)]
    public string NewTitle { get; set; } = null!;

    [MaxLength(600)]
    public string? OldMeta { get; set; }

    [MaxLength(600)]
    public string? NewMeta { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EvaluatedAt { get; set; }

    public SeoTitleTestStatus Status { get; set; }

    public double BaselineCtr { get; set; }

    public double BaselinePosition { get; set; }

    public int BaselineImpressions { get; set; }

    public double? ResultCtr { get; set; }

    public double? ResultPosition { get; set; }

    public int? ResultImpressions { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }
}
