using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Smart;

// ═══════════════════════════════════════════════════════════════════════════
//  دور نوزدهم – جدول‌های «محتوای هوشمند» و «فروش هوشمند»
//  همهٔ جدول‌ها ستون Guid ساده دارند (بدون کلید خارجی) تا حذف محصول/مقاله هیچ‌وقت به خاطر این جدول‌ها گیر نکند؛
//  ردیف یتیم بی‌خطر است و کار شبانه پاکش می‌کند.
//  ایندکس‌ها: Configurations/SmartContentConfiguration.cs
//  کجا استفاده می‌شوند؟
//   • SmartTask             ← صف کارهای هوشمند (عکس‌یاب، مشخصات، جمع‌بندی نظرها، مقاله، دسته). کارگر: Web/Infrastructure/Smart/SmartWorker.cs
//   • SmartResearchCache    ← کش ۳۰ روزهٔ تحقیق اینترنتی (B22)؛ یک جست‌وجو دو بار به اینترنت نمی‌رود.
//   • ProductInsight        ← جمع‌بندی نظرها/نکات کلیدی هر محصول (B10–B13). فقط وضعیت «تأییدشده» در سایت نمایش داده می‌شود.
//   • BlogPlanItem          ← تقویم محتوای بلاگ (B18).
//   • CustomerReminder      ← یادآوری‌های بعد از خرید: درخواست نظر (C19) و تمدید کالای مصرفی (C20).
//   • SmartContentConfig    ← تنظیمات همهٔ این امکانات + کلید هش‌شدهٔ «دستیار رایانهٔ مالک» (A31).
//   • SmartImageFeedback    ← دور بیستم: عکس‌هایی که کارشناس برای هر محصول تأیید/رد کرده (فهرست سیاه + رتبهٔ منبع عکس).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>نوع کار هوشمند. مثال: <see cref="ProductImages"/> = پیدا کردن عکس‌های تازه برای یک محصول.</summary>
public enum SmartTaskKind : byte
{
    None = 0,

    /// <summary>عکس‌یاب چندمنبعی (B1–B4).</summary>
    ProductImages = 1,

    /// <summary>دریافت و ادغام مشخصات فنی از چند منبع (B7–B9).</summary>
    ProductSpecs = 2,

    /// <summary>جمع‌بندی نظرهای خریداران، نکات کلیدی و توضیح کوتاه (B10–B13).</summary>
    ProductInsight = 3,

    /// <summary>«تکمیل هوشمند همه‌چیز» یک محصول (B20): عکس + مشخصات + جمع‌بندی + سئو.</summary>
    ProductComplete = 4,

    /// <summary>عکس، توضیح و پرسش‌های متداول یک دسته (B19).</summary>
    CategoryComplete = 5,

    /// <summary>نوشتن یک مقالهٔ بلاگ از تقویم محتوا (B14–B17).</summary>
    BlogArticle = 6,

    /// <summary>عکس شاخص یک مقاله.</summary>
    BlogImage = 7,

    /// <summary>
    /// «توضیحات محصول» حرفه‌ای محصول با HTML ساختاریافته (دور بیستم): تیتر، خلاصهٔ بررسی، جدول، نقاط قوت/پیش از خرید بدانید و جمع‌بندی.
    /// </summary>
    ProductReview = 8,
}

/// <summary>هدف کار: محصول، دسته یا مقاله.</summary>
public enum SmartTarget : byte
{
    None = 0,
    Product = 1,
    Category = 2,
    BlogPost = 3,
    BlogPlan = 4,
}

/// <summary>وضعیت کار در صف.</summary>
public enum SmartTaskStatus : byte
{
    /// <summary>در صف؛ کارگر برمی‌دارد.</summary>
    Queued = 0,

    /// <summary>در حال اجرا (اجاره‌دار). اگر اجاره تمام شود دوباره به صف برمی‌گردد.</summary>
    Running = 1,

    /// <summary>انجام شد و نتیجه خودکار اعمال شد (اطمینان بالا).</summary>
    Applied = 2,

    /// <summary>انجام شد ولی نتیجه منتظر تأیید کارشناس است.</summary>
    NeedsReview = 3,

    /// <summary>ناموفق (بعد از چند تلاش). متن خطا در Error.</summary>
    Failed = 4,

    /// <summary>کارشناس لغو کرد.</summary>
    Cancelled = 5,

    /// <summary>کاری لازم نبود (مثلاً محصول قبلاً همه‌چیز را داشت).</summary>
    Skipped = 6,
}

/// <summary>
/// یک کار صف هوشمند. با «اجاره» (LeaseUntil) برداشته می‌شود تا اگر برنامه وسط کار ری‌استارت شد، کار گم نشود.
/// <para>مثال: کارشناس روی «تکمیل هوشمند» محصول X می‌زند ← ردیف (ProductComplete, Product, X) ساخته می‌شود؛
/// کارگر هر چند ثانیه یکی را برمی‌دارد، نتیجه را در ResultJson می‌نویسد و وضعیت را Applied یا NeedsReview می‌کند.</para>
/// </summary>
public class SmartTask : BaseEntity
{
    public SmartTaskKind Kind { get; set; }

    public SmartTarget TargetKind { get; set; }

    public Guid? TargetId { get; set; }

    /// <summary>نام هدف برای نمایش در صف (نام محصول/دسته/عنوان مقاله).</summary>
    [MaxLength(250)]
    public string? TargetTitle { get; set; }

    public SmartTaskStatus Status { get; set; }

    /// <summary>عدد بزرگ‌تر زودتر اجرا می‌شود (کار دستی ۱۰، کار گروهی ۵، کار خودکار شبانه ۰).</summary>
    public int Priority { get; set; }

    /// <summary>ورودی اضافهٔ کار (JSON). مثال: {"query":"هدفون هایلو T15","autoApply":true}</summary>
    public string? PayloadJson { get; set; }

    /// <summary>نتیجهٔ کار (JSON): عکس‌های پیدا شده، مشخصات پیشنهادی، … برای صفحهٔ بررسی.</summary>
    public string? ResultJson { get; set; }

    /// <summary>خلاصهٔ فارسی نتیجه. مثال: «۸ عکس باکیفیت پیدا شد؛ ۲ عکس خودکار اضافه شد».</summary>
    [MaxLength(1000)]
    public string? Summary { get; set; }

    [MaxLength(1000)]
    public string? Error { get; set; }

    public int Attempts { get; set; }

    public Guid? RequestedById { get; set; }

    [MaxLength(150)]
    public string? RequestedByName { get; set; }

    /// <summary>زودتر از این زمان اجرا نمی‌شود (تلاش دوباره با فاصله، کارهای زمان‌بندی‌شده).</summary>
    public DateTimeOffset RunAfter { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>تا این زمان کار در دست یک اجراکننده است.</summary>
    public DateTimeOffset? LeaseUntil { get; set; }

    /// <summary>چه کسی اجرا کرد: شناسهٔ کارگر سرور (نام رایانه:شمارهٔ فرایند)؛ از دور ۲۱ فقط خود سرور اجرا می‌کند.</summary>
    [MaxLength(100)]
    public string? ClaimedBy { get; set; }
}

/// <summary>
/// بازخورد کارشناس روی عکس‌های پیشنهادی (دور بیستم).
/// <para>چرا؟ عکس اشتباه یعنی مشتری یک چیز می‌بیند و چیز دیگری می‌خرد. هر عکسی که کارشناس رد کند (یا «رد همه و جست‌وجوی دوباره» بزند)
/// اینجا ثبت می‌شود و دیگر هرگز برای همان محصول پیشنهاد نمی‌شود – نه با همان آدرس و نه با همان تصویر از سایت دیگر (اثر انگشت dHash).</para>
/// <para>رتبهٔ منبع: نسبت تأیید هر منبع (مثلاً «digikala»، «serp:example.com»، «bing:shop.ir») در همهٔ محصولات؛ منبعی که عکس‌هایش
/// بیشتر تأیید شده، دفعهٔ بعد اول می‌آید (یادگیری از تصمیم‌های خود کارشناس).</para>
/// <para>مثال ردیف: ProductId=…، UrlKey=sha256(آدرس تمیزشده)، Hash=0x8f3c…، Source=«serp:mobile.ir»، Approved=false.</para>
/// <para>محل استفاده: SmartCatalogService (GetImageBlacklistAsync، RecordImageFeedbackAsync، ImageSourceScoresAsync) و SmartImageFinder.</para>
/// </summary>
public class SmartImageFeedback : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>SHA-256 هگز (۶۴ نویسه) آدرس تمیزشدهٔ عکس؛ آدرس خام ذخیره نمی‌شود (کوتاه و یکتا).</summary>
    [Required]
    [MaxLength(64)]
    public string UrlKey { get; set; } = null!;

    /// <summary>اثر انگشت تصویری (dHash ۶۴ بیتی)؛ ۰ = نامعلوم.</summary>
    public long Hash { get; set; }

    /// <summary>کلید منبع. مثال: «digikala»، «supplier»، «serp:mobile.ir»، «basalam»، «bing:shop.ir».</summary>
    [MaxLength(100)]
    public string? Source { get; set; }

    /// <summary>true = کارشناس این عکس را انتخاب کرد؛ false = رد شد (فهرست سیاه همین محصول).</summary>
    public bool Approved { get; set; }
}

/// <summary>
/// کش ماندگار تحقیق اینترنتی (B22). کلید = منبع + پرس‌وجوی یکسان‌شده؛ مثال: «dk-search:هدفون هایلو t15».
/// <para>چرا در پایگاه داده؟ تا بعد از ری‌استارت هم یک محصول دوباره از منابع پرسیده نشود (ادب در برابر منابع، سرعت، حافظهٔ کم).</para>
/// </summary>
public class SmartResearchCache : BaseEntity
{
    [Required]
    [MaxLength(200)]
    public string Key { get; set; } = null!;

    [MaxLength(50)]
    public string? Source { get; set; }

    /// <summary>پاسخ خام یا خلاصه‌شده (JSON/HTML کوتاه‌شده).</summary>
    [Required]
    public string Json { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>دور ۲۹ (#۱۸): وضعیت پیشنهاد ویدیو.</summary>
public enum VideoSuggestionStatus : byte
{
    /// <summary>تازه؛ منتظر بررسی مدیر.</summary>
    New = 0,

    /// <summary>مدیر ویدیو را دانلود و به کالا اضافه کرد.</summary>
    Used = 1,

    /// <summary>مدیر رد کرد (همان کالا نبود یا کیفیت نداشت)؛ دوباره پیشنهاد نمی‌شود.</summary>
    Dismissed = 2,
}

/// <summary>
/// دور ۲۹ (#۱۸) – «ویدیوی پیشنهادی» یک کالا: جایی که ویدیوی معرفی همین کالا پیدا شده (صفحهٔ تأمین‌کننده، آپارات، یوتیوب).
/// فقط پیشنهاد است؛ هیچ ویدیویی خودکار دانلود یا روی سایت گذاشته نمی‌شود. مدیر لینک را باز می‌کند، اگر همان کالا بود دانلود و
/// در «ویدیوهای معرفی محصول» اضافه می‌کند و «استفاده شد» یا «رد» می‌زند.
/// مثال: (کالای «هدفون Sony WH-1000XM5»، aparat، https://www.aparat.com/v/abc12، «آنباکسینگ سونی WH-1000XM5»، امتیاز ۸۵).
/// </summary>
public class ProductVideoSuggestion : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>منبع: supplier، aparat یا youtube.</summary>
    [Required]
    [MaxLength(30)]
    public string Source { get; set; } = null!;

    /// <summary>آدرس صفحه‌ای که ویدیو در آن است (برای باز کردن و دانلود توسط مدیر).</summary>
    [Required]
    [MaxLength(500)]
    public string PageUrl { get; set; } = null!;

    [MaxLength(300)]
    public string? Title { get; set; }

    /// <summary>تصویر کوچک ویدیو (آدرس بیرونی؛ فقط در پنل نمایش داده می‌شود).</summary>
    [MaxLength(500)]
    public string? ThumbnailUrl { get; set; }

    /// <summary>میزان تطبیق عنوان ویدیو با نام کالا (۰ تا ۱۰۰). ویدیوی صفحهٔ خود تأمین‌کننده ۹۵.</summary>
    public int Score { get; set; }

    public VideoSuggestionStatus Status { get; set; }
}

/// <summary>وضعیت جمع‌بندی محصول.</summary>
public enum ProductInsightStatus : byte
{
    /// <summary>ساخته شده، منتظر تأیید؛ در سایت دیده نمی‌شود.</summary>
    Draft = 0,

    /// <summary>تأیید شده (دستی یا خودکار با اطمینان بالا)؛ در صفحهٔ محصول نمایش داده می‌شود.</summary>
    Approved = 1,

    /// <summary>کارشناس رد کرد؛ نمایش داده نمی‌شود و تا ساخت دوباره کنار می‌ماند.</summary>
    Rejected = 2,
}

/// <summary>
/// جمع‌بندی هوشمند یک محصول (B10–B13): خلاصه، مناسب چه کسی/نامناسب برای چه کسی، نقاط قوت و ضعف، نتیجه‌گیری،
/// پرسش‌های متداول، نکات کلیدی و توضیح کوتاه. همهٔ متن‌ها با نویسندهٔ داخلی ساخته می‌شوند و از «محافظ رونویسی»
/// (هیچ ۶ واژهٔ پشت‌سرهمِ مشترک با متن منبع) گذشته‌اند.
/// <para>ستون‌های JSON آرایهٔ رشته‌اند؛ مثال ProsJson: ["کیفیت صدای خوب","شارژدهی طولانی"].</para>
/// </summary>
public class ProductInsight : BaseEntity
{
    public Guid ProductId { get; set; }

    public ProductInsightStatus Status { get; set; }

    [MaxLength(1500)]
    public string? Summary { get; set; }

    public string? ForWhomJson { get; set; }

    public string? NotForJson { get; set; }

    public string? ProsJson { get; set; }

    public string? ConsJson { get; set; }

    [MaxLength(1000)]
    public string? Verdict { get; set; }

    /// <summary>پرسش‌وپاسخ‌ها: [{"q":"…","a":"…"}].</summary>
    public string? FaqJson { get; set; }

    /// <summary>۳ تا ۵ نکتهٔ کلیدی بالای صفحه (B13/C4).</summary>
    public string? HighlightsJson { get; set; }

    /// <summary>توضیح کوتاه پیشنهادی (اگر محصول توضیح کوتاه نداشت خودکار اعمال می‌شود).</summary>
    [MaxLength(1000)]
    public string? ShortDescription { get; set; }

    /// <summary>منابع استفاده‌شده (B12): [{"source":"digikala","url":"…","count":42,"at":"2026-11-05"}].</summary>
    public string? SourcesJson { get; set; }

    /// <summary>تعداد نظرهایی که تحلیل شد (از همهٔ منابع).</summary>
    public int ReviewCount { get; set; }

    /// <summary>میانگین امتیاز × ۱۰ (مثلاً ۴۳ یعنی ۴٫۳ از ۵)؛ null یعنی امتیازی نبود.</summary>
    public int? RatingTenths { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    [MaxLength(150)]
    public string? ApprovedByName { get; set; }
}

/// <summary>نوع مقاله (B14).</summary>
public enum BlogArticleType : byte
{
    /// <summary>راهنمای خرید یک دسته. مثال: «راهنمای خرید هدفون بی‌سیم».</summary>
    BuyingGuide = 0,

    /// <summary>بهترین‌های یک دسته با جدول مقایسه (C22). مثال: «بهترین ساعت‌های هوشمند ۱۴۰۵».</summary>
    BestPicks = 1,

    /// <summary>مقایسهٔ دو محصول. مثال: «مقایسهٔ هایلو T15 و GT1».</summary>
    Comparison = 2,

    /// <summary>آموزش/نکات نگهداری و استفاده.</summary>
    HowTo = 3,

    /// <summary>توضیحات محصول یک محصول.</summary>
    Review = 4,

    /// <summary>دور ۲۶: معرفی کامل یک دسته (انواع، کاربرد، نکتهٔ انتخاب و نگهداری، بازهٔ قیمت). بیشتر مقاله‌ها از این نوع‌اند.
    /// مثال: «همه‌چیز دربارهٔ لوازم خانگی برقی کوچک».</summary>
    CategoryGuide = 5,

    /// <summary>دور ۲۶: مقالهٔ کمپین/جشنواره (صفحهٔ ویژه، تخفیف دسته‌ای یا مناسبت). مثال: «جشنوارهٔ شب یلدا: پیشنهادهای خوراکی».</summary>
    Campaign = 6,

    /// <summary>دور ۲۶: خبر واقعی فروشگاه (کالاهای تازهٔ یک دسته در ماه جاری). مثال: «تازه‌های هدفون در آبان».</summary>
    News = 7,
}

/// <summary>وضعیت یک ردیف تقویم محتوا.</summary>
public enum BlogPlanStatus : byte
{
    Planned = 0,
    Writing = 1,

    /// <summary>از دروازهٔ کیفیت گذشت و منتشر شد.</summary>
    Published = 2,

    /// <summary>نوشته شد ولی به دروازهٔ کیفیت نرسید؛ پیش‌نویس ماند تا کارشناس ببیند.</summary>
    Drafted = 3,

    Skipped = 4,
    Failed = 5,
}

/// <summary>
/// یک ردیف «تقویم محتوا» (B18). مثال: ۱۴۰۵/۰۸/۲۰ ← «راهنمای خرید پاوربانک» (BuyingGuide، دستهٔ پاوربانک).
/// </summary>
public class BlogPlanItem : BaseEntity
{
    public DateTimeOffset PlannedFor { get; set; }

    public BlogArticleType ArticleType { get; set; }

    [Required]
    [MaxLength(250)]
    public string Topic { get; set; } = null!;

    [MaxLength(250)]
    public string? Keyword { get; set; }

    /// <summary>دستهٔ محصول مرتبط (برای کارت‌های محصول و جدول بهترین‌ها).</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>محصول‌های اصلی (برای مقایسه/نقد): شناسه‌ها با «,».</summary>
    [MaxLength(500)]
    public string? ProductIds { get; set; }

    public BlogPlanStatus Status { get; set; }

    public Guid? PostId { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    /// <summary>امتیاز دروازهٔ کیفیت (۰ تا ۱۰۰).</summary>
    public int? QualityScore { get; set; }

    /// <summary>منشأ ایده: «auto» (برنامه‌ریز خودکار)، «demand» (جست‌وجوی بی‌نتیجهٔ مشتری‌ها) یا «manual».</summary>
    [MaxLength(50)]
    public string? Origin { get; set; }
}

/// <summary>نوع یادآوری بعد از خرید.</summary>
public enum CustomerReminderKind : byte
{
    /// <summary>درخواست ثبت نظر چند روز بعد از تحویل (C19).</summary>
    ReviewRequest = 0,

    /// <summary>یادآوری تمدید کالای مصرفی (C20).</summary>
    Replenish = 1,
}

public enum CustomerReminderStatus : byte
{
    Pending = 0,
    Sent = 1,

    /// <summary>دیگر لازم نبود (مثلاً مشتری خودش نظر داده یا دوباره خریده).</summary>
    Skipped = 2,

    Cancelled = 3,
}

/// <summary>
/// یک یادآوری بعد از خرید. یکتا: (نوع، سفارش، محصول) تا یک پیام هرگز دو بار فرستاده نشود.
/// <para>برای درخواست نظر، ProductId همان کالای سفارش است؛ مشتری برای هر سفارش حداکثر یک پیام می‌گیرد (اولین کالای بدون نظر).</para>
/// </summary>
public class CustomerReminder : BaseEntity
{
    public CustomerReminderKind Kind { get; set; }

    public Guid UserId { get; set; }

    public Guid OrderId { get; set; }

    public Guid ProductId { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public CustomerReminderStatus Status { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }
}

/// <summary>
/// تنظیمات امکانات هوشمند (یک ردیف). گزینه‌ها JSON هستند تا افزودن گزینهٔ تازه مهاجرت پایگاه داده نخواهد؛
/// کلاس گزینه‌ها: Core/Services/Smart/SmartOptions.cs.
/// <para>دور ۲۱: ستون‌های «دستیار محلی» حذف شدند (مهاجرت Round21Autonomy).</para>
/// </summary>
public class SmartContentConfig : BaseEntity
{
    [Required]
    public string OptionsJson { get; set; } = "{}";

    /// <summary>آخرین اجرای برنامه‌ریز تقویم محتوا.</summary>
    public DateTimeOffset? LastPlanAt { get; set; }

    /// <summary>آخرین ساخت «خلاصهٔ روزانه» (C26).</summary>
    public DateTimeOffset? LastBriefAt { get; set; }
}
