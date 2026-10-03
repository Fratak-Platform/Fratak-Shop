using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.DTOs.Categories.Query;

public class CategoryDto : BaseDto
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? ImageName { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public SeoMetaDataDto? Seo { get; set; }
    public List<SubCategoryDto> SubCategories { get; set; } = [];

    /// <summary>تعداد محصولات فعال این دسته‌بندی (برای نمایش در پنل و فروشگاه).</summary>
    public int ProductCount { get; set; }

    public Guid? ParentId { get; set; }

    public string? ParentName { get; set; }

    /// <summary>مسیر کامل دسته‌بندی برای نمایش در فهرست‌ها.</summary>
    public string? Path { get; set; }
}

/// <summary>دسته‌بندی هدر/فوتر همراه با فرزندانش.</summary>
public class CategoryMenuDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? ImageName { get; set; }
    public int ProductCount { get; set; }
    public List<CategoryMenuDto> Children { get; set; } = [];
}

/// <summary>صفحه دسته‌بندی با فهرست محصولات و اطلاعات سئو.</summary>
public class CategoryDetailDto : CategoryDto
{
    public List<ProductCardDto> Products { get; set; } = [];
}

public class CategoryFilterParams : BaseFilterParam
{
    public string? Search { get; set; }
    public bool OnlyActive { get; set; } = true;
    public Guid? ParentId { get; set; }
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
}