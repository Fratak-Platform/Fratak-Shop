using Fratak_Shop.Core.DTOs.Comments.Command;
using Fratak_Shop.Core.DTOs.Comments.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Comments;

public interface ICommentService
{
    Task<OperationResult> CreateCommentAsync(CreateCommentCommand command);
    Task<OperationResult> EditCommentAsync(EditCommentCommand command);
    Task<OperationResult> DeleteCommentAsync(Guid id);
    Task<OperationResult> ApproveCommentAsync(Guid id);
    Task<OperationResult> RejectCommentAsync(Guid id);

    Task<CommentDto?> GetCommentByIdAsync(Guid id);
    /// <param name="onlyApproved">برای وب‌سایت: فقط دیدگاه‌ها و پاسخ‌های تاییدشده.</param>
    Task<List<CommentDto>> GetCommentsByPostAsync(Guid blogPostId, bool onlyApproved = false);
    /// <param name="onlyApproved">برای وب‌سایت: فقط دیدگاه‌ها و پاسخ‌های تاییدشده.</param>
    Task<List<CommentDto>> GetCommentsByProductAsync(Guid productId, bool onlyApproved = false);
    Task<List<CommentDto>> GetRepliesAsync(Guid parentId);
    Task<CommentFilterResult> GetCommentsByFilterAsync(CommentFilterParams filterParams);

    /// <summary>The customer received this product in a delivered order.</summary>
    Task<bool> HasPurchasedAsync(Guid userId, Guid productId);

    /// <summary>The customer already has a (pending or approved) review for this product.</summary>
    Task<bool> HasReviewedAsync(Guid userId, Guid productId);
}
