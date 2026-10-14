using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.DTOs.Products.Command;

/// <summary>
/// تصویر محصول در فرم ایجاد/ویرایش محصول.
/// </summary>
public class ProductImageCommand
{
    public string ImageName { get; set; } = null!;
    public string? AltText { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>
/// ویژگی محصول (دیکشنری خصوصیات) در فرم ایجاد/ویرایش محصول.
/// </summary>
public class ProductFeatureCommand
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public FeatureValueType ValueType { get; set; }
    public int DisplayOrder { get; set; }
}

/// <summary>
/// دستور ایجاد محصول.
/// </summary>
public class CreateProductCommand
{
    public Guid CategoryId { get; set; }
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
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? PublishedAt { get; set; }
    public SeoMetaData? Seo { get; set; }

    public List<ProductImageCommand> Images { get; set; } = [];
    public List<ProductFeatureCommand> Features { get; set; } = [];
}

/// <summary>
/// دستور ویرایش محصول. تصاویر و ویژگی‌های ارسال‌شده جایگزین مقادیر قبلی می‌شوند.
/// </summary>
public class EditProductCommand
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
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
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? PublishedAt { get; set; }
    public SeoMetaData? Seo { get; set; }

    public List<ProductImageCommand> Images { get; set; } = [];
    public List<ProductFeatureCommand> Features { get; set; } = [];
}
