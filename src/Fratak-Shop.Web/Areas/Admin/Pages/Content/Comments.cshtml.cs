using Fratak_Shop.Core.DTOs.Comments.Query;
using Fratak_Shop.Core.Services.Comments;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Content;

[Authorize(Policy = PolicyNames.Content)]
public class CommentsModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    ICommentService comments)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public CommentFilterResult Result { get; private set; } = null!;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "status")]
    public CommentStatus? Status { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageId { get; set; } = 1;

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "دیدگاه‌ها";

        Result = await comments.GetCommentsByFilterAsync(new CommentFilterParams
        {
            Search = Search,
            Status = Status,
            PageId = PageId,
            Take = 15,
        });
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid id)
    {
        var result = await comments.ApproveCommentAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Content/Comments", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id)
    {
        var result = await comments.RejectCommentAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Content/Comments", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await comments.DeleteCommentAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Content/Comments", new { area = "Admin" });
    }
}
