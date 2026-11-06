using Fratak_Shop.Data.Entities.Comments;

namespace Fratak_Shop.Core.DTOs.Comments.Command;

public class CreateCommentCommand
{
    public Guid UserId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? BlogPostId { get; set; }
    public Guid? ParentId { get; set; }
    public CommentType Type { get; set; }
    public string AuthorName { get; set; } = null!;
    public string AuthorPhone { get; set; } = null!;
    public string? Title { get; set; }
    public string Content { get; set; } = null!;
    public byte? Rating { get; set; }
    public bool? IsRecommended { get; set; }

    /// <summary>Already sanitized and stored review photos (files under /uploads/comments).</summary>
    public List<CommentImageInput> Images { get; set; } = [];
}

public record CommentImageInput(string FileName, string ContentType, int SizeBytes);