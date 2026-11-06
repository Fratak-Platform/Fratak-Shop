using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Brands.Query;

public class BrandDto : BaseDto
{
    public string Name { get; set; } = null!;
    public string? EnglishName { get; set; }
    public string Slug { get; set; } = null!;
    public string? LogoName { get; set; }
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public SeoMetaDataDto? Seo { get; set; }

    /// <summary>تعداد محصولات فعال برند (فقط در کوئری‌های فروشگاهی پر می‌شود).</summary>
    public int ProductCount { get; set; }
}
