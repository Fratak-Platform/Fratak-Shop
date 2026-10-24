using Fratak_Shop.Core.DTOs.Comments.Query;

namespace Fratak_Shop.Core.Mappers.Comment;

public static class CommentMapper
{
    public static CommentDto Map(this Data.Entities.Comments.Comment comment)
    {
        return new CommentDto
        {
            Id = comment.Id,
            CreatedAt = comment.CreatedAt,
            UpdatedAt = comment.UpdatedAt,
            IsDeleted = comment.IsDeleted,
            DeletedAt = comment.DeletedAt,
            UserId = comment.UserId,
            ProductId = comment.ProductId,
            BlogPostId = comment.BlogPostId,
            Type = comment.Type,
            AuthorName = comment.AuthorName,
            AuthorPhone = comment.AuthorPhone,
            Title = comment.Title,
            Content = comment.Content,
            Rating = comment.Rating,
            IsRecommended = comment.IsRecommended,
            Status = comment.Status,
            IsVerifiedPurchase = comment.IsVerifiedPurchase,
            HelpfulCount = comment.HelpfulCount,
            NotHelpfulCount = comment.NotHelpfulCount,
            Images = comment.Images.OrderBy(i => i.DisplayOrder).Select(i => i.FileName).ToList(),
            Replies = comment.Replies
                .Select(r => new CommentReplyDto
                {
                    Id = r.Id,
                    CreatedAt = r.CreatedAt,
                    UpdatedAt = r.UpdatedAt,
                    IsDeleted = r.IsDeleted,
                    DeletedAt = r.DeletedAt,
                    UserId = r.UserId,
                    ProductId = r.ProductId,
                    ParentId = (Guid)r.ParentId!,
                    BlogPostId = r.BlogPostId,
                    Type = r.Type,
                    AuthorName = r.AuthorName,
                    AuthorPhone = r.AuthorPhone,
                    Title = r.Title,
                    Content = r.Content,
                    Rating = r.Rating,
                    IsRecommended = r.IsRecommended,
                    Status = r.Status,
                    IsVerifiedPurchase = r.IsVerifiedPurchase,
                    HelpfulCount = r.HelpfulCount,
                    NotHelpfulCount = r.NotHelpfulCount,
                })
                .ToList()
        };
    }

    public static List<CommentDto> Map(this List<Data.Entities.Comments.Comment> comments)
    {
        return comments.Select(c => c.Map()).ToList();
    }
}
