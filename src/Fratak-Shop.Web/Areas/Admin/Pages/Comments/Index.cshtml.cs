using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Comments;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Comments;

public sealed class AdminCommentRow
{
    public Guid Id { get; init; }
    public CommentType Type { get; init; }
    public CommentStatus Status { get; init; }
    public string AuthorName { get; init; } = "";
    public string AuthorPhone { get; init; } = "";
    public Guid? UserId { get; init; }
    public string? Title { get; init; }
    public string Content { get; init; } = "";
    public byte? Rating { get; init; }
    public bool? IsRecommended { get; init; }
    public bool IsVerifiedPurchase { get; init; }
    public int Helpful { get; init; }
    public int NotHelpful { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? TargetTitle { get; init; }
    public string? TargetUrl { get; init; }
    public Guid? ParentId { get; init; }
    public string? ParentContent { get; init; }
    public List<string> Images { get; init; } = [];
    public List<AdminCommentReply> Replies { get; init; } = [];
}

public sealed record AdminCommentReply(Guid Id, string AuthorName, string Content, CommentStatus Status, DateTimeOffset CreatedAt);

[AdminPermission(AdminPermissions.CommentsModerate)]
public partial class IndexModel(IAdminDataService data, ICommentService comments, ISiteContext site, IReviewSentimentService sentiment,
    Fratak_Shop.Core.Services.Events.IShopEvents events, IQuestionAnswerAdvisor advisor) : AdminPageModel
{
    /// <summary>برچسب‌هایی مثل {FirstName} در پاسخ آماده (فقط برچسب‌های بی‌خطر برای نمایش عمومی جایگزین می‌شوند).</summary>
    [global::System.Text.RegularExpressions.GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial global::System.Text.RegularExpressions.Regex PublicPlaceholderRegex();

    private const int PageSize = 25;

    [BindProperty(SupportsGet = true, Name = "status")] public string? StatusFilter { get; set; }
    [BindProperty(SupportsGet = true, Name = "type")] public CommentType? TypeFilter { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "rating")] public byte? Rating { get; set; }
    [BindProperty(SupportsGet = true, Name = "product")] public Guid? ProductId { get; set; }
    [BindProperty(SupportsGet = true, Name = "unanswered")] public bool Unanswered { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedList<AdminCommentRow> Items { get; private set; } = new([], 0, 1, PageSize);
    public Dictionary<CommentStatus, int> Counts { get; private set; } = new();
    public int UnansweredQuestions { get; private set; }

    /// <summary>دور ۲۴ (پیشنهاد ۵۴): احساس هر دیدگاه این صفحه (نشان «منفی/مثبت» و موضوع شکایت).</summary>
    public Dictionary<Guid, Fratak_Shop.Core.Services.Learning.SentimentResult> Sentiments { get; private set; } = [];

    /// <summary>دور ۲۵: پیش‌نویس‌های پاسخ هوشمند برای پرسش‌های بی‌پاسخ همین صفحه (کلید = شناسهٔ پرسش).</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<QuestionAnswerSuggestion>> Drafts { get; private set; } = new Dictionary<Guid, IReadOnlyList<QuestionAnswerSuggestion>>();

    public async Task OnGetAsync()
    {
        var all = data.Query<Comment>();
        Counts = await all.GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        UnansweredQuestions = await all.CountAsync(c => c.Type == CommentType.Question && c.ParentId == null && c.Status == CommentStatus.Approved && !c.Replies.Any());

        var status = StatusFilter switch
        {
            null or "" or "pending" => CommentStatus.Pending,
            "approved" => CommentStatus.Approved,
            "rejected" => CommentStatus.Rejected,
            _ => (CommentStatus?)null,
        };
        StatusFilter ??= "pending";

        var query = all;
        if (Unanswered)
        {
            query = query.Where(c => c.Type == CommentType.Question && c.ParentId == null && c.Status == CommentStatus.Approved && !c.Replies.Any());
        }
        else if (status is { } s)
        {
            query = query.Where(c => c.Status == s);
        }
        if (TypeFilter is { } t) query = query.Where(c => c.Type == t);
        if (Rating is { } r) query = query.Where(c => c.Rating == r);
        if (ProductId is { } pid) query = query.Where(c => c.ProductId == pid);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = SearchText.NormalizeDigits(Search.Trim());
            query = query.Where(c => c.Content.Contains(term) || c.AuthorName.Contains(term) || c.AuthorPhone.Contains(term)
                                     || (c.Product != null && c.Product.Name.Contains(term)));
        }

        var total = await query.CountAsync();
        var pageNumber = ClampPage(PageNumber, total, PageSize);
        var rows = await query
            .OrderBy(c => c.Status == CommentStatus.Pending ? 0 : 1)
            .ThenByDescending(c => c.CreatedAt)
            .Skip((pageNumber - 1) * PageSize).Take(PageSize)
            .Select(c => new AdminCommentRow
            {
                Id = c.Id,
                Type = c.Type,
                Status = c.Status,
                AuthorName = c.AuthorName,
                AuthorPhone = c.AuthorPhone,
                UserId = c.UserId,
                Title = c.Title,
                Content = c.Content,
                Rating = c.Rating,
                IsRecommended = c.IsRecommended,
                IsVerifiedPurchase = c.IsVerifiedPurchase,
                Helpful = c.HelpfulCount,
                NotHelpful = c.NotHelpfulCount,
                CreatedAt = c.CreatedAt,
                TargetTitle = c.Product != null ? c.Product.Name : c.BlogPost != null ? c.BlogPost.Title : null,
                TargetUrl = c.Product != null ? "/product/" + c.Product.Slug : c.BlogPost != null ? "/blog/" + c.BlogPost.Slug : null,
                ParentId = c.ParentId,
                ParentContent = c.Parent != null ? c.Parent.Content : null,
                Images = c.Images.OrderBy(i => i.DisplayOrder).Select(i => i.FileName).ToList(),
                Replies = c.Replies.OrderBy(x => x.CreatedAt).Select(x => new AdminCommentReply(x.Id, x.AuthorName, x.Content, x.Status, x.CreatedAt)).ToList(),
            })
            .ToListAsync();

        Items = new PagedList<AdminCommentRow>(rows, total, pageNumber, PageSize);
        Sentiments = await sentiment.AnalyzeAsync(rows.Where(r => r.Type != CommentType.Question)
            .Select(r => (r.Id, (string?)((r.Title + " " + r.Content).Trim()), r.Rating, r.IsRecommended)), HttpContext.RequestAborted);
        var open = rows.Where(r => r.Type == CommentType.Question && r.ParentId is null && r.Replies.All(x => x.Status != CommentStatus.Approved)).Select(r => r.Id).ToList();
        if (open.Count > 0)
            Drafts = await advisor.SuggestAsync(open, HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostModerateAsync(Guid id, string action)
    {
        ShowResult(await ApplyAsync(id, action));
        return BackToList();
    }

    public async Task<IActionResult> OnPostBulkAsync(List<Guid> ids, string action)
    {
        if (ids.Count == 0)
        {
            ErrorMessage = "هیچ دیدگاهی انتخاب نشده است.";
            return BackToList();
        }

        int done = 0, failed = 0;
        foreach (var id in ids.Distinct().Take(200))
        {
            if (Succeeded(await ApplyAsync(id, action))) done++;
            else failed++;
        }

        if (done > 0) SuccessMessage = $"{done} دیدگاه به‌روزرسانی شد.";
        if (failed > 0) ErrorMessage = $"{failed} مورد انجام نشد (مثلاً دیدگاه دارای پاسخ قابل حذف نیست).";
        return BackToList();
    }

    /// <summary>Answer a question / review in the name of the shop (approved immediately, shown as «پاسخ فروشگاه»).</summary>
    public async Task<IActionResult> OnPostReplyAsync(Guid id, string? content)
    {
        content = XssSecurity.ToPlainText(content ?? "").Trim();
        if (content.Length is < 2 or > 3000)
        {
            ErrorMessage = "متن پاسخ را (حداکثر ۳۰۰۰ نویسه) وارد کنید.";
            return BackToList();
        }

        var parent = await data.Query<Comment>().FirstOrDefaultAsync(c => c.Id == id);
        if (parent is null)
            return NotFound();
        if (parent.ParentId is not null)
        {
            ErrorMessage = "به پاسخ‌ها نمی‌توان پاسخ داد؛ به دیدگاه اصلی پاسخ دهید.";
            return BackToList();
        }

        // پاسخ دیدگاه عمومی است؛ پس فقط نام نویسنده و نام فروشگاه جایگزین می‌شود (نه موبایل یا موجودی کیف پول).
        if (content.Contains('{'))
        {
            var name = string.IsNullOrWhiteSpace(parent.AuthorName) ? "کاربر" : parent.AuthorName.Trim();
            content = PublicPlaceholderRegex().Replace(content, m => m.Groups[1].Value.ToLowerInvariant() switch
            {
                "firstname" or "name" or "fullname" => name,
                "sitename" => site.Settings.SiteName,
                _ => m.Value,
            });
        }

        // Answering implies the question/review is fine to publish.
        if (parent.Status != CommentStatus.Approved)
            await comments.ApproveCommentAsync(parent.Id);

        var reply = new Comment
        {
            UserId = ActorId,
            ProductId = parent.ProductId,
            BlogPostId = parent.BlogPostId,
            ParentId = parent.Id,
            Type = parent.Type,
            AuthorName = site.Settings.SiteName,
            AuthorPhone = Access.PhoneNumber,
            Content = content,
            Status = CommentStatus.Approved,
        };
        var result = await data.AddAsync(reply);
        if (Succeeded(result))
        {
            SuccessMessage = "پاسخ فروشگاه منتشر شد.";
            // دور ۲۵: پاسخ به پرسش کالا ← اعلان به پرسش‌کننده (سایت + پوش).
            if (parent.Type == CommentType.Question)
                await events.QuestionAnsweredAsync(reply.Id);
        }
        else
            ErrorMessage = result.Message ?? "ثبت پاسخ ناموفق بود.";
        return BackToList();
    }

    private async Task<OperationResult> ApplyAsync(Guid id, string action) => action switch
    {
        "approve" => await comments.ApproveCommentAsync(id),
        "reject" => await comments.RejectCommentAsync(id),
        "delete" => await comments.DeleteCommentAsync(id),
        _ => OperationResult.Error("عملیات نامعتبر است."),
    };
}
