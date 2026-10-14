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
    Task<List<CommentDto>> GetCommentsByPostAsync(Guid blogPostId);
    Task<List<CommentDto>> GetCommentsByProductAsync(Guid productId);
    Task<List<CommentDto>> GetRepliesAsync(Guid parentId);
    Task<CommentFilterResult> GetCommentsByFilterAsync(CommentFilterParams filterParams);
}
