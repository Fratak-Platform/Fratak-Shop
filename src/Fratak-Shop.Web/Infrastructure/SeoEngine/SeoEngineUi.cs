using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Web.Infrastructure.SeoEngine;

/// <summary>
/// برچسب‌ها و رنگ‌های مشترک صفحه‌های موتور سئو در پنل (Seo/Engine، Seo/Review، Seo/Analyze).
/// مثال: <c>SeoEngineUi.ScoreBadge(72)</c> → «a-badge-yellow».
/// </summary>
public static class SeoEngineUi
{
    /// <summary>همهٔ بخش‌های قابل پیشنهاد به ترتیب نمایش.</summary>
    public static readonly SeoFields[] FieldOrder =
    [
        SeoFields.FocusKeyword, SeoFields.Title, SeoFields.MetaDescription, SeoFields.Slug,
        SeoFields.Description, SeoFields.Faq, SeoFields.ImageAlt,
    ];

    public static string KindTitle(SeoEntityKind kind) => kind switch
    {
        SeoEntityKind.Product => "محصول",
        SeoEntityKind.Category => "دسته‌بندی",
        SeoEntityKind.Brand => "برند",
        SeoEntityKind.BlogPost => "مقاله",
        SeoEntityKind.ContentPage => "صفحهٔ محتوا",
        SeoEntityKind.StaticPage => "صفحهٔ ثابت",
        _ => "صفحه",
    };

    public static string FieldTitle(SeoFields field) => field switch
    {
        SeoFields.Title => "عنوان سئو",
        SeoFields.MetaDescription => "توضیحات متا",
        SeoFields.FocusKeyword => "کلمهٔ کلیدی",
        SeoFields.Slug => "نامک (آدرس)",
        SeoFields.Description => "متن صفحه",
        SeoFields.Faq => "پرسش‌وپاسخ",
        SeoFields.ImageAlt => "متن جایگزین تصویرها",
        // ترکیب چند بخش (مثلاً بررسی‌ای که هم عنوان و هم توضیحات را درست می‌کند) → «عنوان سئو، توضیحات متا».
        _ => string.Join("، ", FieldOrder.Where(f => f != field && field.HasFlag(f)).Select(FieldTitle)),
    };

    /// <summary>سبز ≥ ۸۰، زرد ۵۰ تا ۷۹، قرمز زیر ۵۰.</summary>
    public static string ScoreBadge(int score) => score >= 80 ? "a-badge-green" : score >= 50 ? "a-badge-yellow" : "a-badge-red";

    public static string LevelBadge(SeoCheckLevel level) => level switch
    {
        SeoCheckLevel.Good => "a-badge-green",
        SeoCheckLevel.Ok => "a-badge-yellow",
        SeoCheckLevel.Bad => "a-badge-red",
        _ => "a-badge-gray",
    };

    public static string LevelTitle(SeoCheckLevel level) => level switch
    {
        SeoCheckLevel.Good => "خوب",
        SeoCheckLevel.Ok => "قابل بهبود",
        SeoCheckLevel.Bad => "مشکل",
        _ => "اطلاع",
    };

    public static string LevelIcon(SeoCheckLevel level) => level switch
    {
        SeoCheckLevel.Good => "CheckCircle",
        SeoCheckLevel.Ok => "Info",
        SeoCheckLevel.Bad => "Warning",
        _ => "Info",
    };

    public static string StatusTitle(SeoSuggestionStatus status) => status switch
    {
        SeoSuggestionStatus.Pending => "منتظر بررسی",
        SeoSuggestionStatus.Applied => "اعمال شد",
        SeoSuggestionStatus.Rejected => "رد شد",
        SeoSuggestionStatus.Superseded => "نسخهٔ جدیدتر دارد",
        SeoSuggestionStatus.Stale => "کهنه (صفحه عوض شد)",
        _ => status.ToString(),
    };

    public static string StatusBadge(SeoSuggestionStatus status) => status switch
    {
        SeoSuggestionStatus.Pending => "a-badge-sky",
        SeoSuggestionStatus.Applied => "a-badge-green",
        SeoSuggestionStatus.Rejected => "a-badge-red",
        _ => "a-badge-gray",
    };

    public static string SourceTitle(SeoResearchKind kind) => kind switch
    {
        SeoResearchKind.Suggest => "پیشنهاد جست‌وجو (گوگل/بینگ)",
        SeoResearchKind.Serp => "نتایج برتر (بینگ/داک‌داک‌گو)",
        SeoResearchKind.Page => "خواندن صفحهٔ رقبا",
        SeoResearchKind.Link => "بررسی لینک‌های بیرونی",
        _ => kind.ToString(),
    };

    /// <summary>آدرس عمومی امن (نامک فارسی percent-encode می‌شود).</summary>
    public static string PublicUrl(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[..(slash + 1)] + Uri.EscapeDataString(path[(slash + 1)..]);
    }

    /// <summary>مسیر صفحهٔ تحلیل. مثال: /admin/seo/analyze?kind=Product&amp;id=…</summary>
    public static string AnalyzeUrl(SeoEntityKind kind, Guid id) => $"/admin/seo/analyze?kind={kind}&id={id}";

    /// <summary>نوع یک خط دلیل: ✓ (درست شد)، ⚠ (کار انسانی) یا توضیح.</summary>
    public static string ReasonClass(string reason) =>
        reason.StartsWith('✓') ? "a-up" : reason.StartsWith('⚠') ? "a-down" : "";

    /// <summary>طول نمایشی عنوان کامل در نتیجهٔ گوگل (عنوان + « | نام‌سایت»).</summary>
    public static string FullTitle(string title, string siteName) =>
        string.IsNullOrWhiteSpace(siteName) || title.Contains(siteName, StringComparison.Ordinal) ? title : $"{title} | {siteName}";
}

/// <summary>
/// دادهٔ پیش‌نمایش اشتراک (کارت تلگرام/واتس‌اپ/اینستاگرام/ایکس) – فاز ۸ب.
/// ساخته می‌شود در AnalyzeModel.BuildShareAsync و نمایش با Areas/Admin/Pages/Shared/_SharePreview.cshtml.
/// مثال: new SharePreviewModel("گوشی X | فراتک", "خرید گوشی X با گارانتی…", "/uploads/products/x.webp", "fratak.ir", "/product/x").
/// </summary>
public sealed record SharePreviewModel(string Title, string Description, string ImageUrl, string Host, string Path)
{
    /// <summary>پیام‌رسان‌ها عنوان را حدود ۶۵ و توضیح را حدود ۱۶۰ نویسه نشان می‌دهند.</summary>
    public const int TitleLimit = 65, DescriptionLimit = 160;

    public bool TitleTooLong => Title.Length > TitleLimit;
    public bool DescriptionTooLong => Description.Length > DescriptionLimit;

    /// <summary>متن کوتاه‌شده با «…» همان‌طور که در کارت دیده می‌شود.</summary>
    public static string Cut(string text, int limit) => text.Length <= limit ? text : text[..(limit - 1)].TrimEnd() + "…";
}
