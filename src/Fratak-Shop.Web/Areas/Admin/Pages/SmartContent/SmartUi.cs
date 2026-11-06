using System.Text.Json;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Infrastructure.Smart;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// برچسب‌ها و رنگ‌های فارسی صفحه‌های «محتوای هوشمند» پنل (دور ۱۹). مثال: <c>SmartUi.KindTitle(SmartTaskKind.ProductImages)</c> ← «عکس کالا».
/// </summary>
public static class SmartUi
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string KindTitle(SmartTaskKind kind) => kind switch
    {
        SmartTaskKind.ProductImages => "عکس کالا",
        SmartTaskKind.ProductSpecs => "مشخصات کالا",
        SmartTaskKind.ProductInsight => "خلاصهٔ خرید",
        SmartTaskKind.ProductComplete => "تکمیل هوشمند کالا",
        SmartTaskKind.CategoryComplete => "تکمیل دسته",
        SmartTaskKind.BlogArticle => "نوشتن مقاله",
        SmartTaskKind.BlogImage => "تصویر مقاله",
        SmartTaskKind.ProductReview => "توضیحات محصول",
        _ => "نامشخص",
    };

    public static (string Title, string Badge) Status(SmartTaskStatus status) => status switch
    {
        SmartTaskStatus.Queued => ("در صف", "a-badge-gray"),
        SmartTaskStatus.Running => ("در حال انجام", "a-badge-blue"),
        SmartTaskStatus.Applied => ("انجام شد", "a-badge-green"),
        SmartTaskStatus.NeedsReview => ("منتظر تأیید شما", "a-badge-yellow"),
        SmartTaskStatus.Failed => ("ناموفق", "a-badge-red"),
        SmartTaskStatus.Cancelled => ("لغو شد", "a-badge-gray"),
        SmartTaskStatus.Skipped => ("لازم نبود", "a-badge-gray"),
        _ => ("—", "a-badge-gray"),
    };

    public static (string Title, string Badge) PlanStatus(BlogPlanStatus status) => status switch
    {
        BlogPlanStatus.Planned => ("برنامه‌ریزی‌شده", "a-badge-gray"),
        BlogPlanStatus.Writing => ("در حال نوشتن", "a-badge-blue"),
        BlogPlanStatus.Published => ("منتشر شد", "a-badge-green"),
        BlogPlanStatus.Drafted => ("پیش‌نویس (منتظر بازبینی)", "a-badge-yellow"),
        BlogPlanStatus.Skipped => ("رد شد", "a-badge-gray"),
        BlogPlanStatus.Failed => ("ناموفق", "a-badge-red"),
        _ => ("—", "a-badge-gray"),
    };

    public static string ArticleTypeTitle(BlogArticleType type) => type switch
    {
        BlogArticleType.BuyingGuide => "راهنمای خرید",
        BlogArticleType.BestPicks => "بهترین‌ها",
        BlogArticleType.Comparison => "مقایسه",
        BlogArticleType.HowTo => "آموزشی",
        BlogArticleType.Review => "بررسی",
        _ => "—",
    };

    /// <summary>آدرس صفحهٔ هدف یک کار در پنل. مثال: (Product، id) ← /admin/smart/product/{id}.</summary>
    public static string? TargetUrl(SmartTarget target, Guid? id) => id is null ? null : target switch
    {
        SmartTarget.Product => $"/admin/smart/product/{id}",
        SmartTarget.Category => "/admin/smart/categories",
        SmartTarget.BlogPost => $"/admin/blog/edit/{id}",
        SmartTarget.BlogPlan => "/admin/smart/blog",
        _ => null,
    };

    /// <summary>خواندن امن نتیجهٔ یک کار (JSON خراب ← null).</summary>
    public static SmartTaskResult? Result(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<SmartTaskResult>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Serialize(SmartTaskResult result) => JsonSerializer.Serialize(result, Json);
}
