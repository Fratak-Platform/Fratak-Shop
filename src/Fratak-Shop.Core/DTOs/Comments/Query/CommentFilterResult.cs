using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Comments;

namespace Fratak_Shop.Core.DTOs.Comments.Query;

public class CommentFilterParams : BaseFilterParam
{
    public CommentType? CommentType { get; set; }
    public CommentStatus? Status { get; set; }
    public string? Search { get; set; }
}

public class CommentFilterResult : BaseFilter<CommentDto, CommentFilterParams>;
