using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Categories.Query;

public class CategoryDto : BaseDto
{
    public Guid? ParentId { get; set; }
    public string? ParentName { get; set; }
    public string? ParentSlug { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? ImageName { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public SeoMetaDataDto? Seo { get; set; }
    public List<SubCategoryDto> SubCategories { get; set; } = [];

    /// <summary>دور ۲۳: کلید آیکن دسته (Icons.cs). خالی ← نمای سایت آیکن پیش‌فرض «Tag» را نشان می‌دهد.</summary>
    public string? IconKey { get; set; }

    /// <summary>دور ۲۳: دستهٔ سیستمی («دسته‌بندی نشده») – هرگز در منو، متن یا نقشهٔ سایت.</summary>
    public bool IsSystem { get; set; }

    /// <summary>دور ۲۳: تعداد کالای فعال (خود دسته + زیردسته‌ها؛ فقط در فهرست منو پر می‌شود).</summary>
    public int ProductCount { get; set; }
}

public class SubCategoryDto : BaseDto
{
    public Guid ParentId { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? ImageName { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public SeoMetaDataDto? Seo { get; set; }

    /// <summary>دور ۲۳: کلید آیکن.</summary>
    public string? IconKey { get; set; }

    /// <summary>دور ۲۳: تعداد کالای فعال (فقط در فهرست منو پر می‌شود).</summary>
    public int ProductCount { get; set; }
}