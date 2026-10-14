using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.DTOs.Blogs.Posts.Command;

public class EditBlogPostCommand
{
    public Guid Id { get; set; }
    public Guid AuthorId { get; set; }
    public Guid BlogCategoryId { get; set; }
    public string Title { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Excerpt { get; set; }
    public string Content { get; set; } = null!;
    public string? ImageName { get; set; }
    public string? ImageAlt { get; set; }
    public int ViewCount { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public SeoMetaData? Seo { get; set; }
}