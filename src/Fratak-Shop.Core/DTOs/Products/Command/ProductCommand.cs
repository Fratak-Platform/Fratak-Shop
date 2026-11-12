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
    public Guid? BrandId { get; set; }
    public string Name { get; set; } = null!;
    public string? EnglishName { get; set; }
    public string Slug { get; set; } = null!;
    public string SKU { get; set; } = null!;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public decimal? DiscountPrice { get; set; }

    /// <summary>Purchase / cost price (internal; profit reports). Never shown in the store.</summary>
    public decimal? PurchasePrice { get; set; }
    public DateTimeOffset? DiscountStartAt { get; set; }
    public DateTimeOffset? DiscountEndAt { get; set; }
    public int StockQuantity { get; set; }
    public string? WarrantyTitle { get; set; }
    public int? WarrantyMonths { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? PublishedAt { get; set; }
    public SeoMetaData? Seo { get; set; }
    public string? Gtin { get; set; }
    public string? Mpn { get; set; }

    /// <summary>Internal: where the shop buys the product (admin panel only).</summary>
    public Guid? SupplierId { get; set; }

    /// <summary>Internal: product page at the supplier – required.</summary>
    public string? SupplierUrl { get; set; }
    public string? SupplierSku { get; set; }
    public string? SupplierNote { get; set; }

    /// <summary>«ناموجود دستی»: از بررسی خودکار تأمین‌کننده خارج شود (مرحلهٔ هفتم – فاز ۵).</summary>
    public bool ExcludeFromSupplierSync { get; set; }

    /// <summary>
    /// دور ۲۲: دستهٔ کالا در سایت مبدأ (درون‌ریزی). فقط شاهد دسته‌بندی هوشمند است؛ مثال: «لوازم جانبی موبایل > پاوربانک».
    /// </summary>
    public string? SourceCategory { get; set; }

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
    public Guid? BrandId { get; set; }
    public string Name { get; set; } = null!;
    public string? EnglishName { get; set; }
    public string Slug { get; set; } = null!;
    public string SKU { get; set; } = null!;
    public string? ShortDescription { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public decimal? DiscountPrice { get; set; }

    /// <summary>Purchase / cost price (internal; profit reports). Never shown in the store.</summary>
    public decimal? PurchasePrice { get; set; }
    public DateTimeOffset? DiscountStartAt { get; set; }
    public DateTimeOffset? DiscountEndAt { get; set; }
    public int StockQuantity { get; set; }
    public string? WarrantyTitle { get; set; }
    public int? WarrantyMonths { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? PublishedAt { get; set; }
    public SeoMetaData? Seo { get; set; }
    public string? Gtin { get; set; }
    public string? Mpn { get; set; }

    /// <summary>Internal: where the shop buys the product (admin panel only).</summary>
    public Guid? SupplierId { get; set; }

    /// <summary>Internal: product page at the supplier – required.</summary>
    public string? SupplierUrl { get; set; }
    public string? SupplierSku { get; set; }
    public string? SupplierNote { get; set; }

    /// <summary>«ناموجود دستی»: از بررسی خودکار تأمین‌کننده خارج شود (مرحلهٔ هفتم – فاز ۵).</summary>
    public bool ExcludeFromSupplierSync { get; set; }

    public List<ProductImageCommand> Images { get; set; } = [];
    public List<ProductFeatureCommand> Features { get; set; } = [];
}
