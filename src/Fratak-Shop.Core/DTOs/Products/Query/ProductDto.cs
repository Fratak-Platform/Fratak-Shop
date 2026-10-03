using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.DTOs.Products.Query;

/// <summary>کارت محصول؛ همان اطلاعاتی که در فهرست‌ها، اسلایدرها و سبد خرید لازم است.</summary>
public class ProductCardDto : BaseDto
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string SKU { get; set; } = null!;
    public string? ShortDescription { get; set; }
    public string? ImageName { get; set; }
    public string? ImageAlt { get; set; }
    public decimal Price { get; set; }
    public decimal? DiscountPrice { get; set; }
    public decimal FinalPrice { get; set; }
    public int DiscountPercent { get; set; }
    public bool HasDiscount { get; set; }
    public int StockQuantity { get; set; }
    public bool IsInStock { get; set; }
    public bool IsActive { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsNew { get; set; }
    public int ViewCount { get; set; }
    public int SoldCount { get; set; }
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategorySlug { get; set; }
    public string? WarrantyTitle { get; set; }
    public double RatingValue { get; set; }
    public int RatingCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>درصد موجودی برای نمایش نوار پیشرفت در پنل مدیریت.</summary>
    public int StockPercent => StockQuantity <= 0 ? 0 : Math.Min(100, StockQuantity);
}

/// <summary>صفحه جزئیات محصول با تصاویر، مشخصات، امتیازها و محصولات مرتبط.</summary>
public class ProductDetailDto : ProductCardDto
{
    public string? Description { get; set; }
    public int? WarrantyMonths { get; set; }
    public DateTimeOffset? DiscountStartAt { get; set; }
    public DateTimeOffset? DiscountEndAt { get; set; }
    public string? SeoTitle { get; set; }
    public SeoMetaData? Seo { get; set; }

    public List<ProductImageDto> Images { get; set; } = [];
    public List<ProductFeatureDto> Features { get; set; } = [];
    public List<ProductCardDto> RelatedProducts { get; set; } = [];
    public List<BreadcrumbDto> Breadcrumbs { get; set; } = [];
    public List<RatingBreakdownDto> RatingBreakdown { get; set; } = [];

    /// <summary>مشخصات به‌صورت کلید/مقدار برای نمایش جدولی در صفحه محصول.</summary>
    public Dictionary<string, string> SpecificationTable =>
        Features
            .Where(feature => feature.ValueType == FeatureValueType.Text)
            .GroupBy(feature => feature.Key)
            .ToDictionary(group => group.Key, group => group.First().Value);
}

/// <summary>مدل کامل محصول برای فرم ویرایش پنل مدیریت.</summary>
public class ProductDto : BaseDto
{
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string SKU { get; set; } = null!;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public decimal? DiscountPrice { get; set; }
    public DateTimeOffset? DiscountStartAt { get; set; }
    public DateTimeOffset? DiscountEndAt { get; set; }
    public int StockQuantity { get; set; }
    public string? WarrantyTitle { get; set; }
    public int? WarrantyMonths { get; set; }
    public bool IsActive { get; set; }
    public bool IsFeatured { get; set; }
    public int ViewCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public SeoMetaData? Seo { get; set; }
    public List<ProductImageDto> Images { get; set; } = [];
    public List<ProductFeatureDto> Features { get; set; } = [];
}

public class ProductImageDto : BaseDto
{
    public string ImageName { get; set; } = null!;
    public string? AltText { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPrimary { get; set; }
}

public class ProductFeatureDto : BaseDto
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public FeatureValueType ValueType { get; set; }
    public int DisplayOrder { get; set; }
}

public class RatingBreakdownDto
{
    public byte Stars { get; set; }
    public int Count { get; set; }
    public int Percent { get; set; }
}

public class BreadcrumbDto
{
    public string Title { get; set; } = null!;
    public string? Url { get; set; }
    public bool IsCurrent { get; set; }
}

/// <summary>ترتیب‌بندی‌های فهرست محصولات فروشگاه.</summary>
public enum ProductSortBy
{
    Newest = 1,
    Oldest = 2,
    Cheapest = 3,
    MostExpensive = 4,
    BestSelling = 5,
    MostViewed = 6,
    MostDiscounted = 7,
    BestRated = 8,
}

public class ProductFilterParams : BaseFilterParam
{
    public string? Search { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategorySlug { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool OnlyInStock { get; set; }
    public bool OnlyDiscounted { get; set; }
    public bool OnlyFeatured { get; set; }
    public ProductSortBy SortBy { get; set; } = ProductSortBy.Newest;

    /// <summary>فقط برای پنل مدیریت: نمایش محصولات غیرفعال.</summary>
    public bool IncludeInactive { get; set; }

    /// <summary>فقط برای پنل مدیریت: فیلتر بر اساس وضعیت فعال بودن.</summary>
    public bool? IsActive { get; set; }

    /// <summary>فقط برای پنل مدیریت: فیلتر محصولات بدون سئو (امتیاز صفر یا خالی).</summary>
    public bool? MissingSeo { get; set; }

    /// <summary>فقط برای پنل مدیریت: فیلتر بر اساس کمبود موجودی.</summary>
    public bool? LowStock { get; set; }
}

public class ProductFilterResult : BaseFilter<ProductCardDto, ProductFilterParams>;
