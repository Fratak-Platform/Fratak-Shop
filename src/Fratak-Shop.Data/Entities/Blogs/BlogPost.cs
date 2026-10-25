using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Data.Entities.Blogs;

public class BlogPost : BaseEntity
{
    public Guid? AuthorId { get; set; }

    public Guid BlogCategoryId { get; set; }

    [Required]
    [MaxLength(250)]
    public string Title { get; set; } = null!;

    [Required]
    [MaxLength(250)]
    public string Slug { get; set; } = null!;

    [MaxLength(1000)]
    public string? Excerpt { get; set; }

    [Required]
    public string Content { get; set; } = null!;

    /// <summary>
    /// File name of the cover image stored on the server.
    /// </summary>
    [MaxLength(250)]
    public string? ImageName { get; set; }

    [MaxLength(250)]
    public string? ImageAlt { get; set; }

    public int ViewCount { get; set; }

    public bool IsPublished { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>A writer sent the draft to the editors (admin panel review queue).</summary>
    public DateTimeOffset? SubmittedForReviewAt { get; set; }

    /// <summary>Editor's feedback to the writer.</summary>
    [MaxLength(1000)]
    public string? ReviewNote { get; set; }

    public SeoMetaData? Seo { get; set; }

    [ForeignKey(nameof(AuthorId))]
    public User? Author { get; set; }

    [ForeignKey(nameof(BlogCategoryId))]
    public BlogCategory BlogCategory { get; set; } = null!;

    public List<Comment> Comments { get; set; } = [];
}
