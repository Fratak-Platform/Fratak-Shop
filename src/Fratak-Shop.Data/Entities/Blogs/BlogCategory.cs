using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Data.Entities.Blogs;

public class BlogCategory : BaseEntity
{
    [Required]
    [MaxLength(250)]
    public string Name { get; set; } = null!;

    [Required]
    [MaxLength(250)]
    public string Slug { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public SeoMetaData? Seo { get; set; }

    public List<BlogPost> BlogPosts { get; set; } = [];
}
