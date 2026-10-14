using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Comments;

namespace Fratak_Shop.Core.DTOs.Comments.Query;

public class CommentDto : BaseDto
{
    public Guid? UserId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? BlogPostId { get; set; }
    public CommentType Type { get; set; }
    public string AuthorName { get; set; } = null!;
    public string AuthorPhone { get; set; } = null!;
    public string? Title { get; set; }
    public string Content { get; set; } = null!;
    public byte? Rating { get; set; }
    public bool? IsRecommended { get; set; }
    public CommentStatus Status { get; set; }
    public List<CommentReplyDto> Replies { get; set; } = [];
}

public class CommentReplyDto : BaseDto
{
    public Guid? UserId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid ParentId { get; set; }
    public Guid? BlogPostId { get; set; }
    public CommentType Type { get; set; }
    public string AuthorName { get; set; } = null!;
    public string AuthorPhone { get; set; } = null!;
    public string? Title { get; set; }
    public string Content { get; set; } = null!;
    public byte? Rating { get; set; }
    public bool? IsRecommended { get; set; }
    public CommentStatus Status { get; set; }
}