using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Core.DTOs.Products.Query;

/// <summary>
/// جزئیات کامل محصول همراه تصاویر، ویژگی‌ها و اطلاعات سئو.
/// </summary>
public class ProductDto : BaseDto
{
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategorySlug { get; set; }

    public Guid? BrandId { get; set; }
    public string? BrandName { get; set; }
    public string? BrandEnglishName { get; set; }
    public string? BrandSlug { get; set; }

    public string Name { get; set; } = null!;
    public string? EnglishName { get; set; }
    public string Slug { get; set; } = null!;
    public string SKU { get; set; } = null!;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }

    public decimal Price { get; set; }
    public decimal? DiscountPrice { get; set; }
    public DateTimeOffset? DiscountStartAt { get; set; }
    public DateTimeOffset? DiscountEndAt { get; set; }

    /// <summary>قیمت نهایی؛ در صورت فعال بودن بازه تخفیف برابر قیمت با تخفیف است.</summary>
    public decimal FinalPrice { get; set; }

    /// <summary>مبلغ تخفیف محصول (قیمت اصلی منهای قیمت نهایی).</summary>
    public decimal DiscountAmount { get; set; }

    public bool HasActiveDiscount { get; set; }

    /// <summary>درصد تخفیف فعال (عدد صحیح ۰ تا ۱۰۰).</summary>
    public int DiscountPercent => HasActiveDiscount && Price > 0
        ? (int)Math.Round(DiscountAmount / Price * 100)
        : 0;

    public int StockQuantity { get; set; }
    public bool IsInStock => StockQuantity > 0;

    public string? WarrantyTitle { get; set; }
    public int? WarrantyMonths { get; set; }

    public bool IsActive { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public int ViewCount { get; set; }
    public int SalesCount { get; set; }

    /// <summary>میانگین امتیاز دیدگاه‌های تاییدشده (۰ یعنی بدون امتیاز).</summary>
    public double AverageRating { get; set; }

    /// <summary>تعداد دیدگاه‌های تاییدشده.</summary>
    public int ReviewCount { get; set; }

    public SeoMetaDataDto? Seo { get; set; }

    public List<ProductImageDto> Images { get; set; } = [];
    public List<ProductFeatureDto> Features { get; set; } = [];
}

public class ProductImageDto : BaseDto
{
    public Guid ProductId { get; set; }
    public string ImageName { get; set; } = null!;
    public string? AltText { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPrimary { get; set; }
}

public class ProductFeatureDto : BaseDto
{
    public Guid ProductId { get; set; }
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public FeatureValueType ValueType { get; set; }
    public int DisplayOrder { get; set; }
}
