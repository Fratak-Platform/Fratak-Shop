using System.Linq.Expressions;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Core.Services.Products;

// ═══════════════════════════════════════════════════════════════════════════
//  امتیاز کامل بودن محصول (مرحلهٔ هفتم – فاز ۵)
//  یک عدد ۰ تا ۱۰۰ که نشان می‌دهد صفحهٔ محصول چقدر کامل است (عکس، توضیحات، مشخصات، سئو، …) و
//  فهرست کارهای باقی‌مانده با راهنمای کوتاه. کاملاً محلی و بدون دیتابیس (قابل تست).
//  استفاده: ستون «کامل بودن» در فهرست محصولات، فیلتر «ناقص‌ها» و کارت چک‌لیست در ویرایش محصول.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// ورودی ارزیابی؛ فقط عددها و طول‌ها (نه متن کامل) تا بتوان مستقیم از کوئری SQL پر کرد.
/// <paramref name="DescriptionLength"/>: طول توضیحات (با HTML؛ تقریبی کافی است).
/// <paramref name="ProductId"/>: فقط برای وصل کردن نتیجه به ردیف فهرست (در ارزیابی نقشی ندارد).
/// </summary>
public sealed record ProductCompletenessInput(
    int NameLength,
    bool HasEnglishName,
    int ShortDescriptionLength,
    int DescriptionLength,
    int ImageCount,
    int ImagesWithAlt,
    bool HasBrand,
    int FeatureCount,
    int SeoTitleLength,
    int SeoDescriptionLength,
    bool HasGtinOrMpn,
    bool HasWarranty,
    bool HasPurchasePrice,
    Guid ProductId = default);

/// <summary>یک مورد چک‌لیست. <paramref name="Weight"/> سهم آن از ۱۰۰ است.</summary>
public sealed record CompletenessItem(string Key, string Title, int Weight, bool Done, string Hint);

/// <summary>نتیجه: امتیاز و فهرست موارد (انجام‌نشده‌ها اول).</summary>
public sealed record CompletenessResult(int Score, IReadOnlyList<CompletenessItem> Items)
{
    /// <summary>سطح برای رنگ نشان: «کامل» ≥ ۸۵، «متوسط» ≥ ۶۰، وگرنه «ناقص».</summary>
    public string Level => Score >= ProductCompleteness.GoodScore ? "good" : Score >= ProductCompleteness.FairScore ? "fair" : "poor";
}

public static class ProductCompleteness
{
    public const int GoodScore = 85;
    public const int FairScore = 60;

    /// <summary>
    /// ارزیابی. وزن‌ها جمعاً ۱۰۰ است؛ موارد مهم‌تر برای فروش (عکس، توضیحات) وزن بیشتری دارند.
    /// مثال: محصول با ۱ عکس، بدون توضیحات و بدون سئو → حدود ۳۵.
    /// </summary>
    public static CompletenessResult Evaluate(ProductCompletenessInput p)
    {
        var items = new List<CompletenessItem>
        {
            new("images", "حداقل ۳ عکس", 15, p.ImageCount >= 3,
                p.ImageCount == 0 ? "محصول عکس ندارد؛ مشتری بدون عکس خرید نمی‌کند." : $"{p.ImageCount} عکس دارد؛ عکس از زاویه‌های مختلف اضافه کنید."),
            new("image", "عکس اصلی", 10, p.ImageCount >= 1, "یک عکس اصلی با کیفیت بارگذاری کنید."),
            new("alt", "متن جایگزین عکس‌ها", 5, p.ImageCount > 0 && p.ImagesWithAlt >= p.ImageCount,
                "برای هر عکس «متن جایگزین» (Alt) بنویسید؛ برای سئوی تصاویر مهم است."),
            new("description", "توضیحات کامل (۶۰۰+ نویسه)", 15, p.DescriptionLength >= 600,
                p.DescriptionLength == 0 ? "توضیحات ندارد." : "توضیحات کوتاه است؛ کاربرد، مزایا و نکات خرید را اضافه کنید."),
            new("short", "توضیح کوتاه", 5, p.ShortDescriptionLength >= 50, "یک خلاصهٔ ۱ تا ۲ جمله‌ای بالای صفحه بنویسید."),
            new("features", "حداقل ۴ مشخصهٔ فنی", 10, p.FeatureCount >= 4, "مشخصات (رنگ، جنس، ابعاد، …) را در بخش ویژگی‌ها وارد کنید."),
            new("brand", "برند", 5, p.HasBrand, "برند محصول را انتخاب کنید (برای فیلتر و اسکیما)."),
            new("seo-title", "عنوان سئو (۳۰ تا ۶۵ نویسه)", 8, p.SeoTitleLength is >= 30 and <= 65, "عنوان سئو را در بازهٔ مناسب بنویسید."),
            new("seo-description", "توضیح متا (۱۲۰ تا ۱۶۰ نویسه)", 8, p.SeoDescriptionLength is >= 120 and <= 160, "توضیح متا را در بازهٔ مناسب بنویسید."),
            new("name", "نام گویا (۱۵+ نویسه)", 4, p.NameLength >= 15, "نام را کامل‌تر بنویسید (نوع + برند + مدل)."),
            new("english", "نام انگلیسی", 3, p.HasEnglishName, "نام انگلیسی برای جست‌وجوی لاتین و نامک مفید است."),
            new("gtin", "بارکد یا شمارهٔ قطعه (GTIN/MPN)", 4, p.HasGtinOrMpn, "بارکد را وارد کنید؛ گوگل برای نمایش محصول از آن استفاده می‌کند."),
            new("warranty", "گارانتی", 3, p.HasWarranty, "وضعیت گارانتی را مشخص کنید (حتی «بدون گارانتی»)."),
            new("cost", "قیمت خرید (داخلی)", 5, p.HasPurchasePrice, "قیمت خرید برای گزارش سود و هشدار زیان لازم است."),
        };

        var score = items.Where(i => i.Done).Sum(i => i.Weight);
        var ordered = items.OrderBy(i => i.Done).ThenByDescending(i => i.Weight).ToList();
        return new CompletenessResult(Math.Clamp(score, 0, 100), ordered);
    }

    /// <summary>
    /// تبدیل محصول به ورودی ارزیابی «داخل SQL» (فقط طول‌ها و شمارش‌ها خوانده می‌شوند، نه متن کامل توضیحات).
    /// استفاده: <c>query.Select(ProductCompleteness.Projection)</c> در فهرست محصولات و صفحهٔ ویرایش.
    /// </summary>
    public static readonly Expression<Func<Product, ProductCompletenessInput>> Projection = p => new ProductCompletenessInput(
        p.Name.Length,
        p.EnglishName != null && p.EnglishName != "",
        p.ShortDescription == null ? 0 : p.ShortDescription.Length,
        p.Description == null ? 0 : p.Description.Length,
        p.Images.Count,
        p.Images.Count(i => i.AltText != null && i.AltText != ""),
        p.BrandId != null,
        p.Features.Count,
        p.Seo == null || p.Seo.Title == null ? 0 : p.Seo.Title.Length,
        p.Seo == null || p.Seo.MetaDescription == null ? 0 : p.Seo.MetaDescription.Length,
        (p.Gtin != null && p.Gtin != "") || (p.Mpn != null && p.Mpn != ""),
        (p.WarrantyTitle != null && p.WarrantyTitle != "") || p.WarrantyMonths != null,
        p.PurchasePrice != null,
        p.Id);

    /// <summary>
    /// دور ۲۳ – «آمادهٔ انتشار»: پیش‌نویسی که هرگز منتشر نشده و همهٔ چیزهای لازم برای یک صفحهٔ فروش خوب را دارد:
    /// دستهٔ واقعی (نه «دسته‌بندی نشده»)، موجودی و قیمت، حداقل یک عکس، توضیحات ۶۰۰+ نویسه، خلاصهٔ ۵۰+ نویسه، ۳+ مشخصه و نام انگلیسی.
    /// <para>سیستم هرگز خودش منتشر نمی‌کند؛ این فقط برچسب و فیلتر «آمادهٔ انتشار» در فهرست محصولات است تا مدیر با یک کلیک منتشر کند.
    /// کالایی که قبلاً منتشر و بعد عمداً غیرفعال شده (PublishedAt دارد) آماده حساب نمی‌شود.</para>
    /// استفاده: <c>query.Where(ProductCompleteness.ReadyToPublish)</c> در فهرست محصولات و خلاصهٔ روزانهٔ کارگر هوشمند.
    /// </summary>
    public static readonly Expression<Func<Product, bool>> ReadyToPublish = p =>
        !p.IsActive && p.PublishedAt == null && !p.Category.IsSystem
        && p.StockQuantity > 0 && p.Price > 0 && p.Images.Any()
        && p.Description != null && p.Description.Length >= 600
        && p.ShortDescription != null && p.ShortDescription.Length >= 50
        && p.Features.Count >= 3
        && p.EnglishName != null && p.EnglishName != "";

    /// <summary>کلاس رنگ نشان برای سطح (پنل مدیریت).</summary>
    public static string LevelCss(string level) => level switch
    {
        "good" => "a-badge-green",
        "fair" => "a-badge-yellow",
        _ => "a-badge-red",
    };

    /// <summary>عنوان فارسی سطح.</summary>
    public static string LevelTitle(string level) => level switch
    {
        "good" => "کامل",
        "fair" => "متوسط",
        _ => "ناقص",
    };
}
