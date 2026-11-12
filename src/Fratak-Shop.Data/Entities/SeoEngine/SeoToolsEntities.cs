using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.SeoEngine;

// ═══════════════════════════════════════════════════════════════════════════
//  ابزارهای سئو (مرحلهٔ هفتم – فاز ۸ب)
//   • SeoLinkIssue          : لینک یا تصویر خراب داخل متن صفحه‌ها (یابندهٔ لینک شکسته).
//   • SearchConsoleQueryStat: آمار «صفحه × عبارت جست‌وجو» از Google Search Console (۲۸ روز اخیر).
//   • SearchConsoleDailyStat: آمار روزانهٔ کل سایت از Search Console (نمودار ۹۰ روزه).
//   • SeoToolsConfig        : تنظیمات این ابزارها (فقط یک ردیف؛ کلید سرویس گوگل رمزنگاری‌شده).
//  منطق: Core/Services/Seo/Tools؛ زمان‌بندی و HTTP: Web/Infrastructure/SeoEngine/SeoToolsInfrastructure.cs؛
//  پنل: /admin/seo/links، /admin/seo/bulk و /admin/seo/search-console.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// نوع مشکل یک لینک. عددها در دیتابیس ذخیره می‌شوند؛ تغییر ندهید.
/// مثال: لینک به محصولی که حذف شده = NotFound؛ لینک به نامک قدیمی که ریدایرکت دارد = Redirected.
/// </summary>
public enum SeoLinkProblem : byte
{
    None = 0,
    /// <summary>صفحه وجود ندارد (۴۰۴) یا محصول/مقالهٔ مقصد حذف یا غیرفعال شده است.</summary>
    NotFound = 1,
    /// <summary>صفحه برای همیشه حذف شده (۴۱۰).</summary>
    Gone = 2,
    /// <summary>آدرس به جای دیگری ریدایرکت دائمی می‌شود؛ بهتر است لینک مستقیم به آدرس نهایی باشد.</summary>
    Redirected = 3,
    /// <summary>سرور مقصد خطا داد (۵xx).</summary>
    ServerError = 4,
    /// <summary>اتصال برقرار نشد (دامنه از کار افتاده، مهلت تمام شد، گواهی نامعتبر).</summary>
    Unreachable = 5,
    /// <summary>تصویر با http:// در سایت https (محتوای ناامن؛ مرورگر ممکن است نمایش ندهد).</summary>
    Insecure = 6,
    /// <summary>سایت مقصد به ربات‌ها اجازه نداد (۴۰۱/۴۰۳/۴۲۹)؛ احتمالاً سالم است، ولی بررسی دستی لازم است.</summary>
    Blocked = 7,
}

/// <summary>
/// یک لینک یا تصویر مشکل‌دار داخل متن یک صفحه. برای هر (صفحه، آدرس) فقط یک ردیف.
/// <para>در هر اجرای بررسی: ردیف‌های تازه ساخته می‌شوند، ردیف‌های موجود به‌روز می‌شوند و لینک‌هایی که دیگر در متن نیستند
/// یا درست شده‌اند حذف می‌شوند. «نادیده گرفتن» فقط تا وقتی که همان لینک در متن باشد می‌ماند.</para>
/// </summary>
public class SeoLinkIssue : BaseEntity
{
    public SeoEntityKind Kind { get; set; }

    public Guid EntityId { get; set; }

    /// <summary>نام صفحه‌ای که لینک داخلش است. مثال: «گوشی سامسونگ A55».</summary>
    [MaxLength(200)]
    public string EntityTitle { get; set; } = null!;

    /// <summary>آدرس عمومی همان صفحه. مثال: /product/galaxy-a55</summary>
    [MaxLength(400)]
    public string Path { get; set; } = null!;

    /// <summary>آدرس دقیقاً همان‌طور که در HTML نوشته شده (برای جایگزینی دقیق).</summary>
    [MaxLength(2048)]
    public string Url { get; set; } = null!;

    /// <summary>هش SHA-256 آدرس (هگز کوچک) برای ایندکس یکتا؛ آدرس ۲۰۴۸ نویسه‌ای در ایندکس جا نمی‌شود.</summary>
    [MaxLength(64)]
    public string UrlHash { get; set; } = null!;

    /// <summary>متن لینک یا alt تصویر. مثال: «راهنمای خرید هدفون».</summary>
    [MaxLength(200)]
    public string? AnchorText { get; set; }

    /// <summary>تصویر (img) است یا لینک (a)؟</summary>
    public bool IsImage { get; set; }

    /// <summary>لینک به خود سایت است؟</summary>
    public bool IsInternal { get; set; }

    public SeoLinkProblem Problem { get; set; }

    /// <summary>کد HTTP (برای لینک‌های بیرونی). مثال: 404.</summary>
    public int? StatusCode { get; set; }

    /// <summary>آدرس پیشنهادی برای جایگزینی (مقصد ریدایرکت). مثال: /product/galaxy-a55-5g</summary>
    [MaxLength(2048)]
    public string? SuggestedUrl { get; set; }

    /// <summary>توضیح فارسی کوتاه. مثال: «محصول مقصد غیرفعال است».</summary>
    [MaxLength(300)]
    public string? Detail { get; set; }

    /// <summary>تعداد تکرار همین آدرس در متن صفحه.</summary>
    public int Occurrences { get; set; } = 1;

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>کارشناس گفته «مشکلی نیست» (مثلاً سایت بیرونی ربات‌ها را می‌بندد ولی برای انسان باز است).</summary>
    public bool IsIgnored { get; set; }
}

/// <summary>
/// آمار یک «صفحه × عبارت جست‌وجو» از Search Console در بازهٔ <see cref="PeriodStart"/> تا <see cref="PeriodEnd"/>.
/// در هر همگام‌سازی همهٔ ردیف‌ها جایگزین می‌شوند (فقط آخرین بازه نگه داشته می‌شود).
/// <para>مثال: Path=/product/galaxy-a55، Query=«قیمت a55»، Impressions=1200، Clicks=36، Position=7.4</para>
/// </summary>
public class SearchConsoleQueryStat : BaseEntity
{
    /// <summary>مسیر صفحه، رمزگشایی‌شده و با حروف کوچک، بدون / پایانی (همان قالب SeoScore.Path).</summary>
    [MaxLength(400)]
    public string Path { get; set; } = null!;

    [MaxLength(200)]
    public string Query { get; set; } = null!;

    public int Clicks { get; set; }

    public int Impressions { get; set; }

    /// <summary>نرخ کلیک ۰ تا ۱. مثال: 0.03 = ۳٪.</summary>
    public double Ctr { get; set; }

    /// <summary>میانگین جایگاه در نتایج گوگل (۱ = اول).</summary>
    public double Position { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }
}

/// <summary>آمار روزانهٔ کل سایت در گوگل (برای نمودار و مقایسهٔ دوره‌ها). برای هر روز یک ردیف.</summary>
public class SearchConsoleDailyStat : BaseEntity
{
    public DateOnly Date { get; set; }

    public int Clicks { get; set; }

    public int Impressions { get; set; }

    public double Ctr { get; set; }

    public double Position { get; set; }
}

/// <summary>
/// تنظیمات ابزارهای سئو (فقط یک ردیف). کلید JSON حساب سرویس گوگل با ISecretProtector رمزنگاری می‌شود و هرگز
/// در فرم برنمی‌گردد (فقط ایمیل حساب سرویس نمایش داده می‌شود).
/// </summary>
public class SeoToolsConfig : BaseEntity
{
    // ── Search Console ──

    public bool IsSearchConsoleEnabled { get; set; }

    /// <summary>ویژگی (property) در Search Console. مثال: «sc-domain:fratak.ir» یا «https://fratak.ir/».</summary>
    [MaxLength(300)]
    public string? SearchConsoleSite { get; set; }

    /// <summary>کلید JSON حساب سرویس، رمزنگاری‌شده.</summary>
    public string? SearchConsoleKeyProtected { get; set; }

    /// <summary>ایمیل حساب سرویس (برای نمایش و راهنمای افزودن کاربر در Search Console).</summary>
    [MaxLength(300)]
    public string? ServiceAccountEmail { get; set; }

    /// <summary>
    /// پراکسی اختیاری برای اتصال به گوگل (سرورهای داخل ایران معمولاً به API گوگل دسترسی ندارند).
    /// مثال: http://127.0.0.1:8118 یا socks5://127.0.0.1:1080
    /// </summary>
    [MaxLength(300)]
    public string? SearchConsoleProxy { get; set; }

    public DateTimeOffset? SearchConsoleSyncedAt { get; set; }

    public bool SearchConsoleOk { get; set; }

    /// <summary>نتیجهٔ آخرین همگام‌سازی. مثال: «۱۲٬۴۰۰ ردیف از ۲۸ روز اخیر خوانده شد».</summary>
    [MaxLength(500)]
    public string? SearchConsoleStatus { get; set; }

    // ── یابندهٔ لینک شکسته ──

    public bool IsLinkCheckEnabled { get; set; } = true;

    /// <summary>لینک‌های سایت‌های دیگر هم بررسی شوند؟ (لینک‌های داخلی همیشه و بدون درخواست اینترنتی بررسی می‌شوند.)</summary>
    public bool CheckExternalLinks { get; set; } = true;

    public DateTimeOffset? LinkCheckAt { get; set; }

    /// <summary>خلاصهٔ آخرین بررسی. مثال: «۳۲۰ صفحه، ۱٬۴۵۰ لینک، ۱۲ مشکل (۳ تازه)».</summary>
    [MaxLength(500)]
    public string? LinkCheckSummary { get; set; }
}
