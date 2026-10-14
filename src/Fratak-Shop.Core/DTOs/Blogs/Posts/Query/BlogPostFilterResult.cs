using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Blogs.Posts.Query;

public class BlogPostFilterData : BaseDto
{
    public Guid? AuthorId { get; set; }
    public string AuthorName { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Excerpt { get; set; }
    public string Content { get; set; } = null!;
    public string? ImageName { get; set; }
    public string? ImageAlt { get; set; }
    public int ViewCount { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public BlogCategoryPostDto CategoryPostDto { get; set; } = null!;
}

public class BlogPostFilterParams : BaseFilterParam
{
    public string? Search { get; set; }
    public string? CategorySlug { get; set; } = null!;

    /// <summary>فقط مقالات منتشرشده؛ برای لیست عمومی وب‌سایت.</summary>
    public bool? OnlyPublished { get; set; }
}

public class BlogPostFilterResult : BaseFilter<BlogPostFilterData, BlogPostFilterParams>;