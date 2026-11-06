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
