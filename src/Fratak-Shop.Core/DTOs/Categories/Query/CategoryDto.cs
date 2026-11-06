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