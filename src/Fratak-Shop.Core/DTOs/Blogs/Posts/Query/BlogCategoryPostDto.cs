namespace Fratak_Shop.Core.DTOs.Blogs.Posts.Query;

public class BlogCategoryPostDto
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public Guid Id { get; set; }
}