using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.DTOs.Seo.Command;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// «سئوی هوشمند»: فهرست پیشنهادهای تولیدشده، صفحه بازبینی و اعمال تنها پس از تأیید
/// کارشناس. هیچ پیشنهادی به‌صورت خودکار ذخیره نمی‌شود.
/// </summary>
[Authorize(Policy = PolicyNames.Seo)]
public class AuditsModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public SeoAuditFilterResult Result { get; private set; } = null!;

    public SeoAuditDto? Current { get; private set; }

    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "entityType")]
    public SeoEntityType? EntityTypeFilter { get; set; }

    [BindProperty(SupportsGet = true, Name = "status")]
    public SeoAuditStatus? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageId { get; set; } = 1;

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "سئوی هوشمند";

        if (Id.HasValue)
        {
            Current = await seoAudits.GetAsync(Id.Value);
            ViewData["Title"] = "بازبینی پیشنهاد سئو";
        }

        Result = await seoAudits.GetFilterAsync(new SeoAuditFilterParams
        {
            Search = Search,
            EntityType = EntityTypeFilter,
            Status = StatusFilter,
            PageId = PageId,
            Take = 15,
        });
    }

    /// <summary>اعمال پیشنهاد پس از تأیید کارشناس (تنها راه نوشتن روی محتوا).</summary>
    public async Task<IActionResult> OnPostApplyAsync(Guid id)
    {
        var record = await seoAudits.GetAsync(id);
        if (record is null)
        {
            Notify("پیشنهاد مورد نظر یافت نشد.", "error");
            return RedirectToPage("/Seo/Audits", new { area = "Admin" });
        }

        var result = await seoAudits.ApplyAsync(new ApplySeoProposalCommand
        {
            RecordId = id,
            Proposal = record.Proposal,
            UserId = CurrentUserId,
            ApplySlug = false,
            ApplyContent = false,
            ApplyImageAlts = true,
            MarkAsReviewed = true,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Seo/Audits", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id, string? reason)
    {
        var result = await seoAudits.RejectAsync(new RejectSeoProposalCommand
        {
            RecordId = id,
            Reason = reason,
            UserId = CurrentUserId,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Seo/Audits", new { area = "Admin" });
    }
}
