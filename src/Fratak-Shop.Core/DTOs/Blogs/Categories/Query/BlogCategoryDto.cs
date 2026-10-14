using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Blogs.Categories.Query;

public class BlogCategoryDto : BaseDto
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public SeoMetaDataDto? Seo { get; set; }
}