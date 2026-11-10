using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Products.Query;

/// <summary>
/// سطر لیست محصولات (صفحه محصولات، دسته‌بندی و نتایج جستجو).
/// </summary>
public class ProductFilterData : BaseDto
{
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategorySlug { get; set; }
    public Guid? BrandId { get; set; }
    public string? BrandName { get; set; }
    public string? BrandSlug { get; set; }
    public string Name { get; set; } = null!;
    public string? EnglishName { get; set; }
    public string Slug { get; set; } = null!;
    public string SKU { get; set; } = null!;

    public decimal Price { get; set; }
    public decimal? DiscountPrice { get; set; }
    public decimal FinalPrice { get; set; }
    public bool HasActiveDiscount { get; set; }
    public DateTimeOffset? DiscountEndAt { get; set; }

    /// <summary>درصد تخفیف فعال (عدد صحیح ۰ تا ۱۰۰).</summary>
    public int DiscountPercent => HasActiveDiscount && Price > 0
        ? (int)Math.Round((Price - FinalPrice) / Price * 100)
        : 0;

    public string? PrimaryImageName { get; set; }
    public string? PrimaryImageAlt { get; set; }

    public int StockQuantity { get; set; }
    public bool IsInStock => StockQuantity > 0;
    public bool IsActive { get; set; }

    public int ViewCount { get; set; }
    public int SalesCount { get; set; }

    /// <summary>میانگین امتیاز دیدگاه‌های تاییدشده (۰ یعنی بدون امتیاز).</summary>
    public double AverageRating { get; set; }

    /// <summary>تعداد دیدگاه‌های تاییدشده.</summary>
    public int ReviewCount { get; set; }
}

/// <summary>
/// ترتیب نمایش لیست محصولات.
/// </summary>
public enum ProductSortBy
{
    Newest = 1,
    Oldest = 2,
    Cheapest = 3,
    MostExpensive = 4,
    MostStock = 5,
    MostViewed = 6,
    BestSelling = 7,
    MostDiscounted = 8,

    /// <summary>
    /// دور ۲۴ (رتبه‌بندی یادگیرنده): مرتبط‌ترین؛ کالاهایی که مشتری‌ها بعد از همین جست‌وجو بیشتر رویشان کلیک کرده‌اند بالاتر،
    /// بعد کالاهای موجود و پرفروش. بدون <see cref="ProductFilterParams.LearnedQuery"/> مثل پرفروش‌ترین رفتار می‌کند.
    /// </summary>
    Relevance = 9,
}

/// <summary>
/// پارامترهای فیلتر و صفحه‌بندی لیست محصولات.
/// </summary>
public class ProductFilterParams : BaseFilterParam
{
    /// <summary>جستجو در نام، اسلاگ و کد کالا.</summary>
    public string? Search { get; set; }

    /// <summary>
    /// Search words with their synonyms (from the smart search). When set, it replaces the plain
    /// words of <see cref="Search"/>: every group must match, any alternative of a group may match.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>>? SearchTermGroups { get; set; }

    public Guid? CategoryId { get; set; }

    /// <summary>
    /// اسلاگ دسته‌بندی؛ محصولات زیردسته‌های مستقیم آن هم برگردانده می‌شوند.
    /// </summary>
    public string? CategorySlug { get; set; }

    public Guid? BrandId { get; set; }

    /// <summary>فیلتر چندتایی برند بر اساس اسلاگ.</summary>
    public List<string> BrandSlugs { get; set; } = [];

    /// <summary>فقط محصولات دارای تخفیف فعال.</summary>
    public bool? OnlyDiscounted { get; set; }

    /// <summary>محصولی که باید از نتایج حذف شود (مثلاً محصول جاری).</summary>
    public Guid? ExcludeProductId { get; set; }

    /// <summary>بازه قیمت روی قیمت نهایی (با احتساب تخفیف فعال) اعمال می‌شود.</summary>
    public decimal? MinPrice { get; set; }

    public decimal? MaxPrice { get; set; }

    /// <summary>فقط محصولات موجود در انبار.</summary>
    public bool? OnlyAvailable { get; set; }

    /// <summary>فقط محصولات فعال.</summary>
    public bool? OnlyActive { get; set; }

    public ProductSortBy SortBy { get; set; } = ProductSortBy.Newest;

    /// <summary>Specification filters: feature key → accepted values (see ProductFilterAttribute).</summary>
    public Dictionary<string, List<string>> Specifications { get; set; } = [];

    /// <summary>
    /// دور ۲۴: عبارت نرمال‌شدهٔ جست‌وجو برای مرتب‌سازی <see cref="ProductSortBy.Relevance"/> (از جدول SearchClickStats).
    /// مثال: «هدفون بی سیم». خالی = بدون یادگیری.
    /// </summary>
    public string? LearnedQuery { get; set; }
}

/// <summary>A filterable specification for the facet query.</summary>
public record SpecificationFacetRequest(string Slug, string Title, string FeatureKey, int MaxValues);

/// <summary>Filter box of a specification with the value counts of the current result.</summary>
public class SpecificationFacetDto
{
    public string Slug { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string FeatureKey { get; set; } = null!;
    public List<SpecificationFacetValue> Values { get; set; } = [];
    public bool HasSelection => Values.Any(v => v.IsSelected);
}

public class SpecificationFacetValue
{
    public string Value { get; set; } = null!;
    public int Count { get; set; }
    public bool IsSelected { get; set; }
}

public class ProductFilterResult : BaseFilter<ProductFilterData, ProductFilterParams>;

/// <summary>Brand option of a listing filter box.</summary>
public record BrandFacetDto(string Slug, string Name, string? EnglishName, int Count, bool IsSelected);
