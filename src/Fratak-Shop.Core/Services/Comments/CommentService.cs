using Fratak_Shop.Core.DTOs.Comments.Command;
using Fratak_Shop.Core.DTOs.Comments.Query;
using Fratak_Shop.Core.Mappers.Comment;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Comments;

public class CommentService(
    FratakShopDbContext context,
    Events.IShopEvents events,
    Settings.IStoreSettingService settingService) : ICommentService
{
    /// <summary>Orders that count as a purchase of the product (verified-buyer badge, buyers-only reviews).</summary>
    private static readonly OrderStatus[] PurchasedStatuses = [OrderStatus.Delivered];

    public Task<bool> HasPurchasedAsync(Guid userId, Guid productId) =>
        context.Orders.AnyAsync(o => o.UserId == userId
                                     && PurchasedStatuses.Contains(o.Status)
                                     && o.Items.Any(i => i.ProductId == productId));

    public Task<bool> HasReviewedAsync(Guid userId, Guid productId) =>
        context.Comments.AnyAsync(c => c.UserId == userId
                                       && c.ProductId == productId
                                       && c.ParentId == null
                                       && c.Type == CommentType.Review
                                       && c.Status != CommentStatus.Rejected);

    public async Task<OperationResult> CreateCommentAsync(CreateCommentCommand command)
    {
        if (command.UserId == Guid.Empty)
            return OperationResult.Error("کاربر را وارد کنید.");

        var userExists = await context.Users.AnyAsync(x => x.Id == command.UserId);
        if (!userExists)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(command.AuthorName))
            return OperationResult.Error("نام نویسنده را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.AuthorPhone))
            return OperationResult.Error("شماره تماس را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.Content))
            return OperationResult.Error("متن دیدگاه را وارد کنید.");

        if (command.ProductId is null && command.BlogPostId is null)
            return OperationResult.Error("برای ثبت کامنت باید یک محصول یا پست بلاگ انتخاب شود.");

        if (command.ProductId is not null && command.BlogPostId is not null)
            return OperationResult.Error("یک کامنت نمی‌تواند هم برای محصول و هم برای پست بلاگ باشد.");

        if (command.Rating.HasValue && (command.Rating.Value < 1 || command.Rating.Value > 5))
            return OperationResult.Error("امتیاز دیدگاه باید بین ۱ تا ۵ باشد.");

        if (command.ProductId is not null)
        {
            var productExists = await context.Products.AnyAsync(x => x.Id == command.ProductId.Value);
            if (!productExists)
                return OperationResult.NotFound("محصول مورد نظر یافت نشد.");
        }

        if (command.BlogPostId is not null)
        {
            var postExists = await context.BlogPosts.AnyAsync(x => x.Id == command.BlogPostId.Value);
            if (!postExists)
                return OperationResult.NotFound("مقاله مورد نظر یافت نشد.");
        }

        var isProductReview = command.ProductId is not null && command.ParentId is null && command.Type == CommentType.Review;
        var verifiedPurchase = false;
        // Reviews and answers to product questions get the «خریدار» badge.
        if (command.ProductId is not null && (command.Type == CommentType.Review || command.ParentId is not null))
            verifiedPurchase = await HasPurchasedAsync(command.UserId, command.ProductId.Value);

        if (isProductReview)
        {
            // One review per customer and product (a rejected one may be written again).
            if (await HasReviewedAsync(command.UserId, command.ProductId!.Value))
                return OperationResult.Error("شما قبلاً برای این محصول دیدگاه ثبت کرده‌اید.");

            var settings = await settingService.GetAsync();
            if (settings.IsReviewOnlyForBuyers && !verifiedPurchase)
                return OperationResult.Error("فقط خریداران این محصول می‌توانند دیدگاه ثبت کنند.");

            if (command.Images.Count > 0 && (!settings.IsReviewImageEnabled || command.Images.Count > Math.Max(0, settings.ReviewMaxImages)))
                return OperationResult.Error($"حداکثر {Math.Max(0, settings.ReviewMaxImages)} تصویر برای هر دیدگاه قابل ارسال است.");
        }
        else if (command.Images.Count > 0)
        {
            return OperationResult.Error("ارسال تصویر فقط برای دیدگاه محصول امکان‌پذیر است.");
        }

        if (command.Images.Any(i => string.IsNullOrWhiteSpace(i.FileName) || i.FileName.Length > 100
                                    || i.FileName.Contains('/') || i.FileName.Contains('\\') || i.FileName.Contains("..")))
            return OperationResult.Error("تصویر ارسالی معتبر نیست.");

        if (command.ParentId is not null)
        {
            var parent = await context.Comments
                .FirstOrDefaultAsync(c => c.Id == command.ParentId.Value);

            if (parent is null)
                return OperationResult.NotFound("کامنت والد پیدا نشد.");

            if (parent.ProductId != command.ProductId || parent.BlogPostId != command.BlogPostId)
                return OperationResult.Error("پاسخ باید برای همان محصول یا مقاله کامنت والد ثبت شود.");
        }

        var model = new Comment
        {
            UserId = command.UserId,
            ProductId = command.ProductId,
            BlogPostId = command.BlogPostId,
            ParentId = command.ParentId,
            Type = command.Type,
            AuthorName = command.AuthorName.Trim(),
            AuthorPhone = command.AuthorPhone.Trim(),
            Title = command.Title,
            Content = command.Content.Trim(),
            Rating = command.Rating,
            IsRecommended = command.IsRecommended,
            Status = CommentStatus.Pending,
            IsVerifiedPurchase = verifiedPurchase,
            Images = command.Images
                .Select((image, index) => new CommentImage
                {
                    FileName = image.FileName,
                    ContentType = image.ContentType,
                    SizeBytes = image.SizeBytes,
                    DisplayOrder = index,
                })
                .ToList(),
        };

        context.Comments.Add(model);
        await context.SaveChangesAsync();

        return OperationResult.Success("دیدگاه شما ثبت شد و پس از تایید نمایش داده می‌شود.");
    }

    public async Task<OperationResult> EditCommentAsync(EditCommentCommand command)
    {
        var comment = await context.Comments.AsTracking().FirstOrDefaultAsync(c => c.Id == command.Id);
        if (comment == null)
            return OperationResult.NotFound("کامنت مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(command.AuthorName))
            return OperationResult.Error("نام نویسنده را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.AuthorPhone))
            return OperationResult.Error("شماره تماس را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.Content))
            return OperationResult.Error("متن دیدگاه را وارد کنید.");

        if (command.Rating.HasValue && (command.Rating.Value < 1 || command.Rating.Value > 5))
            return OperationResult.Error("امتیاز دیدگاه باید بین ۱ تا ۵ باشد.");

        if (command.ParentId == command.Id)
            return OperationResult.Error("دیدگاه نمی‌تواند پاسخ خودش باشد.");

        comment.UserId = command.UserId;
        comment.ProductId = command.ProductId;
        comment.BlogPostId = command.BlogPostId;
        comment.ParentId = command.ParentId;
        comment.Type = command.Type;
        comment.AuthorName = command.AuthorName.Trim();
        comment.AuthorPhone = command.AuthorPhone.Trim();
        comment.Title = command.Title;
        comment.Content = command.Content.Trim();
        comment.Rating = command.Rating;
        comment.IsRecommended = command.IsRecommended;

        await context.SaveChangesAsync();

        return OperationResult.Success("کامنت با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteCommentAsync(Guid id)
    {
        var comment = await context.Comments.AsTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (comment == null)
            return OperationResult.NotFound("کامنت مورد نظر یافت نشد.");

        var hasReplies = await context.Comments.AnyAsync(c => c.ParentId == id);
        if (hasReplies)
            return OperationResult.Error("این دیدگاه پاسخ دارد؛ ابتدا پاسخ‌های آن را حذف کنید.");

        context.Comments.Remove(comment);
        await context.SaveChangesAsync();

        return OperationResult.Success("کامنت با موفقیت حذف شد.");
    }

    public async Task<OperationResult> ApproveCommentAsync(Guid id)
    {
        var comment = await context.Comments.AsTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (comment == null)
            return OperationResult.NotFound("کامنت مورد نظر یافت نشد.");

        var wasApproved = comment.Status == CommentStatus.Approved;
        comment.Status = CommentStatus.Approved;
        await context.SaveChangesAsync();

        // Club points for approved product reviews.
        await events.CommentApprovedAsync(comment.Id);

        // دور ۲۵: پاسخ تازه‌تأییدشده به یک پرسش کالا ← اعلان به پرسش‌کننده (تأیید دوباره اعلان تکراری نمی‌فرستد).
        if (comment.ParentId is not null && !wasApproved)
            await events.QuestionAnsweredAsync(comment.Id);

        return OperationResult.Success("کامنت با موفقیت تایید شد.");
    }

    public async Task<OperationResult> RejectCommentAsync(Guid id)
    {
        var comment = await context.Comments.AsTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (comment == null)
            return OperationResult.NotFound("کامنت مورد نظر یافت نشد.");

        comment.Status = CommentStatus.Rejected;
        await context.SaveChangesAsync();

        return OperationResult.Success("کامنت با موفقیت رد شد.");
    }

    public async Task<CommentDto?> GetCommentByIdAsync(Guid id)
    {
        var comment = await context.Comments
            .Include(c => c.Replies)
            .FirstOrDefaultAsync(c => c.Id == id);

        return comment?.Map();
    }

    public async Task<List<CommentDto>> GetCommentsByPostAsync(Guid blogPostId, bool onlyApproved = false)
    {
        var query = onlyApproved
            ? context.Comments.Include(c => c.Replies
                .Where(r => r.Status == CommentStatus.Approved)
                .OrderBy(r => r.CreatedAt))
            : context.Comments.Include(c => c.Replies.OrderBy(r => r.CreatedAt));

        var comments = await query
            .Where(c => c.BlogPostId == blogPostId
                        && c.ParentId == null
                        && (!onlyApproved || c.Status == CommentStatus.Approved))
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return comments.Map();
    }

    public async Task<List<CommentDto>> GetCommentsByProductAsync(Guid productId, bool onlyApproved = false)
    {
        var query = onlyApproved
            ? context.Comments.Include(c => c.Replies
                .Where(r => r.Status == CommentStatus.Approved)
                .OrderBy(r => r.CreatedAt))
            : context.Comments.Include(c => c.Replies.OrderBy(r => r.CreatedAt));

        var comments = await query
            .Include(c => c.Images)
            .AsSplitQuery()
            .Where(c => c.ProductId == productId
                        && c.ParentId == null
                        && (!onlyApproved || c.Status == CommentStatus.Approved))
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var result = comments.Map();

        // Team answers first, then the most helpful customer answers.
        var authorIds = result.SelectMany(c => c.Replies).Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToList();
        var staff = authorIds.Count == 0
            ? new List<Guid>()
            : await context.Users.Where(u => authorIds.Contains(u.Id) && (u.Role != Data.Entities.Users.UserRole.Customer || context.UserAdminRoles.Any(m => m.UserId == u.Id))).Select(u => u.Id).ToListAsync();

        foreach (var comment in result)
        {
            foreach (var reply in comment.Replies)
                reply.IsStaff = reply.UserId.HasValue && staff.Contains(reply.UserId.Value);

            comment.Replies = comment.Replies
                .OrderByDescending(r => r.IsStaff)
                .ThenByDescending(r => r.HelpfulCount - r.NotHelpfulCount)
                .ThenBy(r => r.CreatedAt)
                .ToList();
        }

        return result;
    }

    public async Task<List<CommentDto>> GetRepliesAsync(Guid parentId)
    {
        var replies = await context.Comments
            .Where(c => c.ParentId == parentId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        return replies.Map();
    }

    public async Task<CommentFilterResult> GetCommentsByFilterAsync(CommentFilterParams filterParams)
    {
        var query = context.Comments
            .OrderBy(c => c.CreatedAt)
            .Include(c => c.Replies)
            .AsQueryable();

        if (filterParams.CommentType.HasValue)
            query = query.Where(c => c.Type == filterParams.CommentType.Value);

        if (filterParams.Status.HasValue)
            query = query.Where(c => c.Status == filterParams.Status.Value);

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var search = filterParams.Search.Trim();
            query = query.Where(c => c.AuthorName.Contains(search)
                || (c.Title != null && c.Title.Contains(search))
                || c.Content.Contains(search));
        }

        var skip = (filterParams.PageId - 1) * filterParams.Take;

        var comments = await query
            .Skip(skip)
            .Take(filterParams.Take)
            .ToListAsync();

        var result = new CommentFilterResult
        {
            Data = comments.Map(),
            FilterParams = filterParams,
        };

        result.GeneratePaging(query, filterParams.Take, filterParams.PageId);

        return result;
    }
}
