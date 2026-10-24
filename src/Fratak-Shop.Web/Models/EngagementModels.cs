namespace Fratak_Shop.Web.Models;

/// <summary>Helpful / not helpful buttons of a review or answer.</summary>
public record VoteButtonsModel(Guid CommentId, int HelpfulCount, int NotHelpfulCount, bool? MyVote);
