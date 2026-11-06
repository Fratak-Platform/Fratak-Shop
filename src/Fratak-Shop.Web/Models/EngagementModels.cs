namespace Fratak_Shop.Web.Models;

/// <summary>Helpful / not helpful buttons of a review or answer.</summary>
public record VoteButtonsModel(Guid CommentId, int HelpfulCount, int NotHelpfulCount, bool? MyVote);

/// <summary>1–5 star radio group (CSS only, keyboard accessible).</summary>
public record StarInputModel(string Name, string Title, int Value = 0);
