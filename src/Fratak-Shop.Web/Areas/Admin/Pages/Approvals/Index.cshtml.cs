using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Team;
using Fratak_Shop.Data.Entities.Team;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Team;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.TeamPages;

/// <summary>
/// صفحهٔ «تأییدهای دونفره» (مرحلهٔ هفتم – فاز ۱۰). کارهای حساسی که از سقف تنظیم‌شده بزرگ‌ترند (تغییر دستی کیف پول،
/// پرداخت برداشت، تغییر نقش کارکنان) اینجا منتظر تأیید «همکار دوم» می‌مانند و بعد از تأیید خودکار انجام می‌شوند.
/// درخواست‌دهنده نمی‌تواند درخواست خودش را تأیید کند. تصمیم‌گیری [RequireReauth] دارد.
/// </summary>
[AdminPermission(AdminPermissions.CustomersWallet, AdminPermissions.WithdrawalsManage, AdminPermissions.StaffManage)]
public class ApprovalsModel(IApprovalService approvals, AdminBadgeService badges) : AdminPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Mine { get; set; }

    [BindProperty(SupportsGet = true, Name = "id")]
    public Guid? FocusId { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageNumber { get; set; } = 1;

    public PagedList<ApprovalRequest> Requests { get; private set; } = new([], 0, 1, 30);

    /// <summary>درخواستی که از لینک اعلان باز شده (?id=…) و بالای صفحه نمایش داده می‌شود.</summary>
    public ApprovalRequest? Focus { get; private set; }

    public async Task OnGetAsync()
    {
        ApprovalStatus? status = Status switch
        {
            "all" => null,
            null or "" or "pending" => ApprovalStatus.Pending,
            _ => Enum.TryParse<ApprovalStatus>(Status, true, out var parsed) ? parsed : ApprovalStatus.Pending,
        };
        Requests = await approvals.ListAsync(new ApprovalFilter(status, Mine, ClampPage(PageNumber)), Access);
        if (FocusId is { } id && await approvals.GetAsync(id) is { } focus
            && (focus.RequestedById == ActorId || Can(approvals.PermissionFor(focus.Kind))))
            Focus = focus;
    }

    /// <summary>آیا کارمند فعلی می‌تواند دربارهٔ این درخواست تصمیم بگیرد؟ (در انتظار، منقضی‌نشده، مال خودش نیست، دسترسی دارد)</summary>
    public bool CanDecide(ApprovalRequest request) =>
        request.Status == ApprovalStatus.Pending && request.ExpiresAt > DateTimeOffset.UtcNow
        && request.RequestedById != ActorId && Can(approvals.PermissionFor(request.Kind));

    [RequireReauth]
    public async Task<IActionResult> OnPostDecideAsync(Guid id, bool approve, string? note)
    {
        ShowResult(await approvals.DecideAsync(id, approve, note, Access));
        badges.InvalidatePersonal(ActorId);
        badges.Invalidate();
        return Redirect($"/admin/approvals?id={id}");
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid id)
    {
        ShowResult(await approvals.CancelAsync(id, Access));
        return Redirect("/admin/approvals?mine=true");
    }

    // ── برچسب‌های نمایشی ──

    public static string KindLabel(ApprovalKind kind) => kind switch
    {
        ApprovalKind.WalletAdjust => "تغییر کیف پول",
        ApprovalKind.WithdrawalPaid => "پرداخت برداشت",
        ApprovalKind.StaffRoles => "نقش کارکنان",
        _ => kind.ToString(),
    };

    public static (string Text, string Css) StatusLabel(ApprovalRequest request) => request.Status switch
    {
        ApprovalStatus.Pending when request.ExpiresAt <= DateTimeOffset.UtcNow => ("منقضی", "a-badge-gray"),
        ApprovalStatus.Pending => ("در انتظار تأیید", "a-badge-yellow"),
        ApprovalStatus.Approved => ("تأیید و انجام شد", "a-badge-green"),
        ApprovalStatus.Rejected => ("رد شد", "a-badge-red"),
        ApprovalStatus.Cancelled => ("پس گرفته شد", "a-badge-gray"),
        ApprovalStatus.Failed => ("تأیید شد، اجرا ناموفق", "a-badge-red"),
        ApprovalStatus.Expired => ("منقضی", "a-badge-gray"),
        _ => (request.Status.ToString(), "a-badge-gray"),
    };
}
