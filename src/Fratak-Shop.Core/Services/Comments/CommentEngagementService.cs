using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Comments;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Comments;

/// <summary>
/// Advanced Q&amp;A and reviews: customers answer each other's product questions (buyers get a
/// badge, answers are moderated like any comment) and vote reviews / answers helpful or not.
/// One vote per customer and comment (changeable); own comments can't be voted.
/// </summary>
public interface ICommentEngagementService
{
    Task<OperationResult> AnswerQuestionAsync(Guid userId, Guid questionId, string? content);

    Task<OperationResult<CommentVoteResult>> VoteAsync(Guid userId, Guid commentId, bool helpful);

    /// <summary>The customer's votes on these comments (true = helpful).</summary>
    Task<Dictionary<Guid, bool>> GetUserVotesAsync(Guid userId, IReadOnlyCollection<Guid> commentIds);
}

public sealed record CommentVoteResult(int HelpfulCount, int NotHelpfulCount, bool? MyVote);

public class CommentEngagementService(FratakShopDbContext context, IStoreSettingService settingService, ICommentService comments) : ICommentEngagementService
{
    public async Task<OperationResult> AnswerQuestionAsync(Guid userId, Guid questionId, string? content)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsCustomerAnswerEnabled)
            return OperationResult.Error("پاسخ کاربران به پرسش‌ها در حال حاضر فعال نیست.");

        var text = content?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length < 5)
            return OperationResult.Error("متن پاسخ را بنویسید (حداقل ۵ کاراکتر).");
        if (text.Length > 1000)
            return OperationResult.Error("پاسخ حداکثر می‌تواند ۱۰۰۰ کاراکتر باشد.");

        var question = await context.Comments
            .Where(c => c.Id == questionId && c.Type == CommentType.Question && c.ParentId == null
                        && c.Status == CommentStatus.Approved && c.ProductId != null)
            .Select(c => new { c.ProductId, c.UserId })
            .FirstOrDefaultAsync();
        if (question is null)
            return OperationResult.NotFound("پرسش مورد نظر یافت نشد.");

        if (question.UserId == userId)
            return OperationResult.Error("نمی‌توانید به پرسش خودتان پاسخ دهید.");

        if (await context.Comments.AnyAsync(c => c.ParentId == questionId && c.UserId == userId && c.Status != CommentStatus.Rejected))
            return OperationResult.Error("شما قبلاً به این پرسش پاسخ داده‌اید.");

        var user = await context.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.FirstName, u.LastName, u.PhoneNumber })
            .FirstOrDefaultAsync();
        if (user is null)
            return OperationResult.NotFound("کاربر یافت نشد.");

        var name = $"{user.FirstName} {user.LastName}".Trim();
        var result = await comments.CreateCommentAsync(new DTOs.Comments.Command.CreateCommentCommand
        {
            UserId = userId,
            ProductId = question.ProductId,
            ParentId = questionId,
            Type = CommentType.Question,
            AuthorName = string.IsNullOrEmpty(name) ? "کاربر فراتک" : name,
            AuthorPhone = user.PhoneNumber,
            Content = text,
        });

        return result.IsSuccess()
            ? OperationResult.Success("پاسخ شما ثبت شد و پس از تایید نمایش داده می‌شود.")
            : result;
    }

    public async Task<OperationResult<CommentVoteResult>> VoteAsync(Guid userId, Guid commentId, bool helpful)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsCommentVotingEnabled)
            return OperationResult<CommentVoteResult>.Error("رأی‌دادن به دیدگاه‌ها فعال نیست.");

        var comment = await context.Comments
            .Where(c => c.Id == commentId && c.Status == CommentStatus.Approved && c.ProductId != null)
            .Select(c => new { c.UserId })
            .FirstOrDefaultAsync();
        if (comment is null)
            return OperationResult<CommentVoteResult>.NotFound("دیدگاه مورد نظر یافت نشد.");

        if (comment.UserId == userId)
            return OperationResult<CommentVoteResult>.Error("نمی‌توانید به دیدگاه خودتان رأی دهید.");

        var vote = await context.CommentVotes.AsTracking().FirstOrDefaultAsync(v => v.CommentId == commentId && v.UserId == userId);
        bool? myVote = helpful;
        if (vote is null)
        {
            context.CommentVotes.Add(new CommentVote { CommentId = commentId, UserId = userId, IsHelpful = helpful });
        }
        else if (vote.IsHelpful == helpful)
        {
            // Same button again = take the vote back.
            context.CommentVotes.Remove(vote);
            myVote = null;
        }
        else
        {
            vote.IsHelpful = helpful;
        }

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear(); // double click: the first vote won
        }

        // Counters recomputed from the votes (never drift, even with parallel votes).
        await context.Comments
            .Where(c => c.Id == commentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.HelpfulCount, c => context.CommentVotes.Count(v => v.CommentId == commentId && v.IsHelpful))
                .SetProperty(c => c.NotHelpfulCount, c => context.CommentVotes.Count(v => v.CommentId == commentId && !v.IsHelpful)));

        var counts = await context.Comments
            .Where(c => c.Id == commentId)
            .Select(c => new { c.HelpfulCount, c.NotHelpfulCount })
            .FirstAsync();

        return OperationResult<CommentVoteResult>.Success(new CommentVoteResult(counts.HelpfulCount, counts.NotHelpfulCount, myVote),
            myVote is null ? "رأی شما برداشته شد." : "رأی شما ثبت شد.");
    }

    public async Task<Dictionary<Guid, bool>> GetUserVotesAsync(Guid userId, IReadOnlyCollection<Guid> commentIds)
    {
        if (commentIds.Count == 0)
            return [];

        return await context.CommentVotes
            .Where(v => v.UserId == userId && commentIds.Contains(v.CommentId))
            .ToDictionaryAsync(v => v.CommentId, v => v.IsHelpful);
    }
}
