using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Products.Query;

/// <summary>
/// سطر لیست محصولات (صفحه محصولات، دسته‌بندی و نتایج جستجو).
/// </summary>
public class ProductFilterData : BaseDto
{
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string SKU { get; set; } = null!;

    public decimal Price { get; set; }
    public decimal? DiscountPrice { get; set; }
    public decimal FinalPrice { get; set; }
    public bool HasActiveDiscount { get; set; }

    public string? PrimaryImageName { get; set; }
    public string? PrimaryImageAlt { get; set; }

    public int StockQuantity { get; set; }
    public bool IsActive { get; set; }
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
}

/// <summary>
/// پارامترهای فیلتر و صفحه‌بندی لیست محصولات.
/// </summary>
public class ProductFilterParams : BaseFilterParam
{
    /// <summary>جستجو در نام، اسلاگ و کد کالا.</summary>
    public string? Search { get; set; }

    public Guid? CategoryId { get; set; }

    public string? CategorySlug { get; set; }

    public decimal? MinPrice { get; set; }

    public decimal? MaxPrice { get; set; }

    /// <summary>فقط محصولات موجود در انبار.</summary>
    public bool? OnlyAvailable { get; set; }

    /// <summary>فقط محصولات فعال.</summary>
    public bool? OnlyActive { get; set; }

    public ProductSortBy SortBy { get; set; } = ProductSortBy.Newest;
}

public class ProductFilterResult : BaseFilter<ProductFilterData, ProductFilterParams>;
