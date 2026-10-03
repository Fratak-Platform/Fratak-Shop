using Fratak_Shop.Core.DTOs.Payments.Command;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.DTOs.Payments.Query;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Payments;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Payments;

[Authorize(Policy = PolicyNames.Orders)]
public class BankTransfersModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public BankTransferFilterResult Result { get; private set; } = null!;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "status")]
    public BankTransferStatus? Status { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageId { get; set; } = 1;

    [BindProperty]
    public string? RejectReason { get; set; }

    [BindProperty]
    public string? AdminNote { get; set; }

    [BindProperty]
    public bool NotifyCustomer { get; set; } = true;

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "بررسی فیش‌های کارت به کارت";

        Result = await bankTransfers.GetFilterAsync(new BankTransferFilterParams
        {
            Search = Search,
            Status = Status,
            PageId = PageId,
            Take = 20,
        });
    }

    public async Task<IActionResult> OnPostReviewAsync(Guid id, bool approve)
    {
        var result = await bankTransfers.ReviewAsync(new ReviewBankTransferCommand
        {
            PaymentId = id,
            Approve = approve,
            RejectReason = RejectReason,
            AdminNote = AdminNote,
            ReviewerUserId = CurrentUserId,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Payments/BankTransfers", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostExpireAsync()
    {
        var count = await bankTransfers.ExpireOverdueAsync();
        Notify(count == 0 ? "فیش منقضی‌شده‌ای یافت نشد." : $"{count} فیش منقضی شد.");
        return RedirectToPage("/Payments/BankTransfers", new { area = "Admin" });
    }
}
