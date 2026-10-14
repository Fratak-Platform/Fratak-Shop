using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Comments;

/// <summary>
/// A review, question or blog comment. Exactly one of <see cref="ProductId"/>
/// or <see cref="BlogPostId"/> must be set (enforced by a check constraint).
/// Replies are linked through <see cref="ParentId"/>.
/// </summary>
public class Comment : BaseEntity
{
    public Guid? UserId { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? BlogPostId { get; set; }

    public Guid? ParentId { get; set; }

    public CommentType Type { get; set; }

    [MaxLength(250)]
    public string AuthorName { get; set; } = null!;

    [MaxLength(20)]
    public string AuthorPhone { get; set; } = null!;

    [MaxLength(250)]
    public string? Title { get; set; }

    [Required]
    public string Content { get; set; } = null!;

    [Range(1, 5)]
    public byte? Rating { get; set; }

    public bool? IsRecommended { get; set; }

    public CommentStatus Status { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = new();

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    [ForeignKey(nameof(BlogPostId))]
    public BlogPost? BlogPost { get; set; }

    [ForeignKey(nameof(ParentId))]
    public Comment? Parent { get; set; }

    public List<Comment> Replies { get; set; } = [];
}

public enum CommentType
{
    None = 0,
    Review = 1,
    Question = 2,
    BlogComment = 3,
}

public enum CommentStatus
{
    None = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}
