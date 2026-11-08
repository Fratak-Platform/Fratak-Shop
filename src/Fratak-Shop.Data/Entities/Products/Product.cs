using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Favorites;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Data.Entities.Products;

/// <summary>
/// A single (non-variant) product. All sale, stock and warranty data belongs to
/// the product itself; extra specifications are stored in the
/// <see cref="ProductFeature"/> dictionary.
/// </summary>
public class Product : BaseEntity
{
    public Guid CategoryId { get; set; }

    public Guid? BrandId { get; set; }

    [Required]
    [MaxLength(250)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Latin / original model name shown under the Persian title,
    /// e.g. "Apple Watch Series 7 41mm".
    /// </summary>
    [MaxLength(250)]
    public string? EnglishName { get; set; }

    [Required]
    [MaxLength(250)]
    public string Slug { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string SKU { get; set; } = null!;

    [MaxLength(1000)]
    public string? ShortDescription { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public decimal? DiscountPrice { get; set; }

    /// <summary>
    /// Purchase / cost price of one unit (internal, never shown to customers).
    /// Snapshotted on order items for profit reports.
    /// </summary>
    public decimal? PurchasePrice { get; set; }

    public DateTimeOffset? DiscountStartAt { get; set; }

    public DateTimeOffset? DiscountEndAt { get; set; }

    public int StockQuantity { get; set; }

    [MaxLength(250)]
    public string? WarrantyTitle { get; set; }

    public int? WarrantyMonths { get; set; }

    public SeoMetaData? Seo { get; set; }

    /// <summary>Global trade item number (EAN / UPC barcode) – product rich results.</summary>
    [MaxLength(20)]
    public string? Gtin { get; set; }

    /// <summary>Manufacturer part number – product rich results.</summary>
    [MaxLength(50)]
    public string? Mpn { get; set; }

    // ── Supplier (internal: admin panel only, never rendered on the storefront) ──

    /// <summary>Where the shop buys this product.</summary>
    public Guid? SupplierId { get; set; }

    /// <summary>Exact product page at the supplier (required in the admin form).</summary>
    [MaxLength(1000)]
    public string? SupplierUrl { get; set; }

    [MaxLength(100)]
    public string? SupplierSku { get; set; }

    [MaxLength(500)]
    public string? SupplierNote { get; set; }

    public Supplier? Supplier { get; set; }

    /// <summary>
    /// «ناموجود دستی»: کارمند عمداً این کالا را از بررسی خودکار تأمین‌کننده خارج کرده است
    /// (مثلاً کالا را دیگر نمی‌فروشیم). این محصول در بررسی‌های زمان‌بندی‌شده خوانده نمی‌شود.
    /// </summary>
    public bool ExcludeFromSupplierSync { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>
    /// Number of product page visits; drives the "most viewed" ordering.
    /// </summary>
    public int ViewCount { get; set; }

    /// <summary>
    /// Number of units sold through placed orders; drives the "best selling" ordering.
    /// </summary>
    public int SalesCount { get; set; }

    // ── دور ۲۲: ورود هوشمند کالا (دسته‌بندی ← تکمیل اطلاعات) ──

    /// <summary>
    /// زمانی که «ورود هوشمند» این کالا انجام شد (دسته‌بندی + گذاشتن «تکمیل هوشمند» در صف). null یعنی کالا تازه است و کارگر هوشمند
    /// چند دقیقه بعد از ساخت سراغش می‌رود (فرقی نمی‌کند از پنل ساخته شده باشد، از سایت دیگر درون‌ریزی شده یا کپی شده باشد).
    /// <para>محل استفاده: ProductIntakeService (لایهٔ Core) و SmartWorker. کالاهای موجود هنگام مهاجرت «انجام‌شده» علامت خوردند تا صف شلوغ نشود.</para>
    /// </summary>
    public DateTimeOffset? SmartIntakeAt { get; set; }

    /// <summary>
    /// دستهٔ همین کالا در سایت مبدأ یا فروشگاه‌های بزرگ (مسیر با « > »). مثال: «زیبایی و سلامت > لوازم شخصی برقی > ماساژور».
    /// فقط شاهدی برای دسته‌بندی هوشمند است و هرگز در سایت نمایش داده نمی‌شود.
    /// </summary>
    [MaxLength(300)]
    public string? SourceCategory { get; set; }

    /// <summary>
    /// دستهٔ فعلی را چه کسی گذاشته؟ Manual (پیش‌فرض: ادمین یا دادهٔ قدیمی؛ دسته‌بندی هوشمند هرگز جابه‌جایش نمی‌کند)،
    /// Smart (دستهٔ دقیق را سیستم پیدا کرد)، General (سیستم کالا را موقتاً در دستهٔ کلی‌تر گذاشت؛ با جمع شدن کالاهای مشابه به زیر‌دستهٔ دقیق می‌رود).
    /// </summary>
    public ProductCategorySource CategorySource { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category Category { get; set; } = null!;

    [ForeignKey(nameof(BrandId))]
    public Brand? Brand { get; set; }

    public List<ProductImage> Images { get; set; } = [];

    public List<ProductFeature> Features { get; set; } = [];

    public List<ProductVideo> Videos { get; set; } = [];

    public List<Comment> Comments { get; set; } = [];

    public List<Favorite> Favorites { get; set; } = [];
}

/// <summary>
/// منبع دستهٔ کالا (دور ۲۲). مثال: ماساژور تنها ← General در «لوازم شخصی برقی»؛ با رسیدن سومین ماساژور ← Smart در زیر‌دستهٔ «ماساژور».
/// </summary>
public enum ProductCategorySource
{
    /// <summary>ادمین انتخاب کرده (یا دادهٔ پیش از دور ۲۲).</summary>
    Manual = 0,

    /// <summary>دسته‌بندی هوشمند دستهٔ دقیق را پیدا یا ساخت.</summary>
    Smart = 1,

    /// <summary>دسته‌بندی هوشمند کالا را در دستهٔ کلی‌تر گذاشت (کالای مشابه کافی برای دستهٔ اختصاصی نبود).</summary>
    General = 2,
}
