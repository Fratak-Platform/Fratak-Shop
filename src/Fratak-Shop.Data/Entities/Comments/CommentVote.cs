using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Comments;

/// <summary>"Was this helpful?" vote of a customer on a review or an answer (one per customer).</summary>
public class CommentVote : BaseEntity
{
    public Guid CommentId { get; set; }

    public Guid UserId { get; set; }

    public bool IsHelpful { get; set; }

    [ForeignKey(nameof(CommentId))]
    public Comment Comment { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
