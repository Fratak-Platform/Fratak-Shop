using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Data.Entities.Categories;

public class Category : BaseEntity
{
    public Guid? ParentId { get; set; }

    [Required] 
    [MaxLength(250)]
    public string Name { get; set; } = null!;

    [Required]
    [MaxLength(250)]
    public string Slug { get; set; } = null!;

    public string? Description { get; set; }

    [MaxLength(250)]
    public string? ImageName { get; set; }

    /// <summary>
    /// دور ۲۲: تا ۴ تصویر پیشنهادی دسته (فهرست JSON نام فایل‌ها در پوشهٔ تصویر دسته‌ها). اولی خودکار تصویر دسته می‌شود؛
    /// ادمین در «تکمیل هوشمند دسته‌ها» می‌تواند یکی دیگر را انتخاب یا هر کدام را حذف کند. مثال: ["a.webp","b.webp"].
    /// </summary>
    [MaxLength(2000)]
    public string? ImageOptionsJson { get; set; }

    /// <summary>
    /// دور ۲۳: نام آیکون دسته (از مجموعهٔ آیکون‌های سایت). خالی ← سیستم خودش انتخاب می‌کند (CategoryIcons). مثال: «DeviceMobile».
    /// </summary>
    [MaxLength(40)]
    public string? IconKey { get; set; }

    /// <summary>
    /// دور ۲۳ (مورد ۶۹): کمترین سود این دسته (درصد روی هزینه). خالی ← کف سراسری تنظیمات. زیردسته‌ها از والد ارث می‌برند و
    /// همیشه بیشترینِ کف سراسری و کف دسته‌های بالادستی اعمال می‌شود (کف دسته هیچ‌وقت سود را پایین‌تر از کف سراسری نمی‌برد).
    /// مثال: کف سراسری ۵٪، «گوشی» ۸٪ ← «گوشی سامسونگ» (بدون مقدار) ۸٪. محل استفاده: CategoryMargins.
    /// </summary>
    public int? MinMarginPercent { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// دور ۲۱: دستهٔ سیستمی (فقط «دسته‌بندی نشده» با نامک uncategorized). همیشه غیرفعال و پنهان از سایت؛ حذف‌شدنی نیست.
    /// کالاهای درون‌ریزی‌شده بدون دسته اینجا می‌آیند تا دسته‌بندی هوشمند (SmartCategoryService) جایشان را پیدا کند.
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>دور ۲۱: این دسته را دسته‌بندی هوشمند ساخته است (با اولین کالای فعال خودکار فعال می‌شود).</summary>
    public bool IsAutoCreated { get; set; }

    public SeoMetaData? Seo { get; set; }

    [ForeignKey(nameof(ParentId))]
    public Category? ParentCategory { get; set; }

    public List<Category> SubCategories { get; set; } = [];

    public List<Product> Products { get; set; } = [];
}
