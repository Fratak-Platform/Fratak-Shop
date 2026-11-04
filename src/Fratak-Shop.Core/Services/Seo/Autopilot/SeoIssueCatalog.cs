using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>تعریف یک نوع مشکل سئو.</summary>
/// <param name="Code">کد ثابت (مثال: <c>title-missing</c>).</param>
/// <param name="Title">عنوان فارسی کوتاه برای فهرست کارها.</param>
/// <param name="Fix">راه رفع به زبان ساده.</param>
/// <param name="Severity">شدت.</param>
/// <param name="Impact">اثر تقریبی روی رتبه/ترافیک (۱ تا ۱۰۰) برای مرتب‌سازی کارها.</param>
/// <param name="AutoFixable">خلبان خودکار خودش درستش می‌کند؟</param>
public sealed record SeoIssueInfo(string Code, string Title, string Fix, SeoTaskSeverity Severity, int Impact, bool AutoFixable);

/// <summary>
/// فهرست همهٔ مشکل‌هایی که خزش/بازرس صفحه پیدا می‌کند، با توضیح فارسی و اولویت (مورد ۲۰۷: کارهای اولویت‌دار).
/// <para>کجا استفاده می‌شود؟ ساخت «کارهای سئو» از خزش و نمایش در داشبورد.</para>
/// </summary>
public static class SeoIssueCatalog
{
    private static readonly SeoIssueInfo[] All =
    [
        new("http-error", "صفحه خطا می‌دهد (۴xx/۵xx)", "لینک‌های داخلی به این آدرس اصلاح یا ریدایرکت ۳۰۱ ساخته شود.", SeoTaskSeverity.Critical, 95, true),
        new("redirect-chain", "زنجیرهٔ ریدایرکت", "لینک داخلی مستقیم به مقصد نهایی اشاره کند.", SeoTaskSeverity.Warning, 50, true),
        new("title-missing", "عنوان صفحه خالی است", "عنوان سئو خودکار نوشته می‌شود.", SeoTaskSeverity.Critical, 90, true),
        new("title-long", "عنوان بلندتر از ۶۵ نویسه", "عنوان در نتایج گوگل بریده می‌شود؛ کوتاه شود.", SeoTaskSeverity.Info, 25, true),
        new("title-short", "عنوان خیلی کوتاه", "عنوان کامل‌تر (نوع محصول + برند + مدل) نوشته شود.", SeoTaskSeverity.Info, 20, true),
        new("title-duplicate", "عنوان تکراری با صفحهٔ دیگر", "هر صفحه عنوان یکتا داشته باشد.", SeoTaskSeverity.Warning, 60, true),
        new("meta-missing", "توضیحات متا خالی است", "توضیحات متا خودکار نوشته می‌شود.", SeoTaskSeverity.Warning, 60, true),
        new("meta-long", "توضیحات متا بلند", "حداکثر حدود ۱۶۰ نویسه.", SeoTaskSeverity.Info, 15, true),
        new("meta-short", "توضیحات متا کوتاه", "حداقل حدود ۱۲۰ نویسه با دعوت به اقدام.", SeoTaskSeverity.Info, 15, true),
        new("meta-duplicate", "توضیحات متای تکراری", "هر صفحه توضیحات یکتا داشته باشد.", SeoTaskSeverity.Warning, 40, true),
        new("canonical-missing", "تگ کنونیکال ندارد", "قالب صفحه باید کنونیکال بسازد.", SeoTaskSeverity.Warning, 45, false),
        new("h1-missing", "تیتر H1 ندارد", "هر صفحه دقیقاً یک H1.", SeoTaskSeverity.Warning, 50, false),
        new("h1-multiple", "بیش از یک H1", "H1 های اضافهٔ متن به H2 تبدیل می‌شوند.", SeoTaskSeverity.Info, 25, true),
        new("heading-order", "پرش در ترتیب تیترها", "ترتیب H2→H3→H4 رعایت می‌شود.", SeoTaskSeverity.Info, 10, true),
        new("thin-content", "محتوای کم (زیر ۱۵۰ واژه)", "متن مفید (مشخصات، کاربرد، مقایسه، پرسش‌ها) اضافه شود.", SeoTaskSeverity.Warning, 55, false),
        new("img-alt", "تصویر بدون Alt", "Alt خودکار از نام صفحه و تیتر نزدیک ساخته می‌شود.", SeoTaskSeverity.Warning, 35, true),
        new("img-legacy-format", "تصویر JPG/PNG (نه WebP)", "تبدیل خودکار به WebP.", SeoTaskSeverity.Warning, 35, true),
        new("img-size", "تصویر بدون width/height", "ابعاد تصویر مشخص شود تا صفحه نپرد (CLS).", SeoTaskSeverity.Info, 15, false),
        new("html-comments", "کامنت کد در HTML", "کامنت‌ها از خروجی حذف می‌شوند.", SeoTaskSeverity.Info, 5, true),
        new("mixed-content", "منبع http در صفحهٔ https", "آدرس منبع https شود.", SeoTaskSeverity.Warning, 40, false),
        new("schema-error", "خطای دادهٔ ساختاریافته", "فیلد الزامی اسکیما تکمیل شود.", SeoTaskSeverity.Warning, 45, false),
        new("og-image-missing", "تصویر اشتراک‌گذاری ندارد", "تصویر اوپن‌گراف خودکار ساخته می‌شود.", SeoTaskSeverity.Info, 15, true),
        new("viewport-missing", "تگ viewport ندارد", "قالب صفحه اصلاح شود.", SeoTaskSeverity.Critical, 70, false),
        new("lang-missing", "زبان صفحه مشخص نیست", "ویژگی lang روی html.", SeoTaskSeverity.Info, 10, false),
        new("link-empty-text", "لینک داخلی بدون متن", "متن یا aria-label برای لینک.", SeoTaskSeverity.Info, 10, false),
        new("internal-nofollow", "لینک داخلی nofollow", "لینک داخلی نباید nofollow باشد.", SeoTaskSeverity.Info, 15, false),
        new("orphan", "صفحهٔ یتیم (بدون لینک داخلی)", "لینک داخلی خودکار از صفحه‌های مرتبط.", SeoTaskSeverity.Warning, 45, true),
        new("deep-page", "صفحه بیش از ۴ کلیک از صفحهٔ اصلی", "از دسته/صفحهٔ اصلی لینک بگیرد.", SeoTaskSeverity.Info, 25, true),
        new("slow-page", "پاسخ کند (بیش از ۱٫۵ ثانیه)", "کش و پرس‌وجوهای صفحه بررسی شود.", SeoTaskSeverity.Warning, 40, false),
        new("heavy-page", "HTML سنگین (بیش از ۳۰۰ کیلوبایت)", "اسکریپت/متن اضافه کم شود.", SeoTaskSeverity.Info, 20, false),
        new("near-duplicate", "محتوای تقریباً تکراری", "متن یکی از دو صفحه متمایز یا کنونیکال شود.", SeoTaskSeverity.Warning, 50, false),
        new("noindex-in-sitemap", "صفحهٔ noindex در نقشهٔ سایت", "از نقشهٔ سایت حذف می‌شود.", SeoTaskSeverity.Warning, 30, true),
        new("not-in-sitemap", "صفحهٔ قابل ایندکس در نقشهٔ سایت نیست", "به نقشهٔ سایت اضافه می‌شود.", SeoTaskSeverity.Info, 20, true),
        new("cannibalization", "رقابت دو صفحه روی یک کلمه", "کلمهٔ کلیدی یکی تغییر کند یا ادغام شود.", SeoTaskSeverity.Warning, 55, false),
        new("low-ctr", "نرخ کلیک پایین نسبت به جایگاه", "عنوان/توضیحات بازنویسی و آزمایش می‌شود.", SeoTaskSeverity.Warning, 60, true),
        new("near-page-one", "نزدیک صفحهٔ اول گوگل (جایگاه ۸ تا ۲۰)", "لینک داخلی بیشتر و تکمیل محتوا.", SeoTaskSeverity.Info, 50, true),
        new("traffic-drop", "افت ناگهانی نمایش/کلیک", "تغییرات اخیر و خطاهای صفحه بررسی شود.", SeoTaskSeverity.Critical, 85, false),
        new("cwv-poor", "سرعت واقعی کاربران ضعیف", "تصویر اصلی سبک‌تر و اسکریپت‌ها کمتر شود.", SeoTaskSeverity.Warning, 45, false),
        new("fake-bot", "ربات جعلی گوگل", "بازدیدهای جعلی شناسایی شد (فایروال).", SeoTaskSeverity.Info, 5, false),
        new("ssl-expiring", "گواهی SSL نزدیک انقضا", "تمدید گواهی در هاست.", SeoTaskSeverity.Critical, 90, false),
        new("calendar", "مناسبت پیش رو", "صفحه/مقالهٔ مناسبت از الان آماده شود.", SeoTaskSeverity.Info, 30, false),
        new("content-gap", "جست‌وجوی داخلی بی‌نتیجه", "محصول/مقاله برای این جست‌وجو اضافه شود.", SeoTaskSeverity.Info, 35, false),
        new("stale-content", "محتوای قدیمی پربازدید", "مقاله به‌روز شود (تاریخ، قیمت، مدل‌ها).", SeoTaskSeverity.Info, 30, false),
        new("empty", "صفحهٔ خالی", "صفحه محتوایی برنگرداند.", SeoTaskSeverity.Critical, 80, false),
    ];

    private static readonly Dictionary<string, SeoIssueInfo> ByCode = All.ToDictionary(i => i.Code, StringComparer.Ordinal);

    /// <summary>همهٔ تعریف‌ها.</summary>
    public static IReadOnlyList<SeoIssueInfo> Items => All;

    /// <summary>تعریف یک کد؛ کد ناشناخته ← تعریف عمومی (هرگز خطا نمی‌دهد).</summary>
    public static SeoIssueInfo Get(string code) =>
        ByCode.TryGetValue(code, out var info) ? info : new SeoIssueInfo(code, code, "بررسی شود.", SeoTaskSeverity.Info, 10, false);
}
