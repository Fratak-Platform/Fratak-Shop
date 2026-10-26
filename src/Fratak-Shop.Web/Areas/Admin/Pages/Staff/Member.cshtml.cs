using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Staff;

[AdminPermission(AdminPermissions.StaffManage)]
public class MemberModel(
    IAdminAccessService access,
    IAdminDataService data,
    global::Fratak_Shop.Core.Services.Team.IApprovalService approvals,
    global::Fratak_Shop.Core.Services.Team.IStaffTotpService totp,
    global::Fratak_Shop.Core.Services.Settings.IStoreSettingService settingsService) : AdminPageModel
{
    public User Member { get; private set; } = null!;
    public List<AdminRoleDto> Roles { get; private set; } = [];
    public HashSet<Guid> Assigned { get; private set; } = [];
    public bool MemberIsOwner { get; private set; }
    public IReadOnlyList<string> EffectivePermissions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var user = await data.Query<User>().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound();
        Member = user;
        Roles = await access.GetRolesAsync();
        Assigned = (await access.GetUserRolesAsync(id)).Select(r => r.Id).ToHashSet();
        var memberAccess = await access.GetAccessAsync(id);
        MemberIsOwner = memberAccess.IsOwner;
        EffectivePermissions = memberAccess.IsOwner
            ? AdminPermissions.All.Select(p => p.Title).ToList()
            : AdminPermissions.All.Where(p => memberAccess.Permissions.Contains(p.Key)).Select(p => p.Title).ToList();
        return Page();
    }

    /// <summary>
    /// ذخیرهٔ نقش‌ها. فاز ۱۰: اگر «تأیید دوم برای تغییر نقش» روشن باشد، فقط درخواست ثبت می‌شود و بعد از تأیید
    /// یک همکار دیگرِ دارای «مدیریت کارکنان» اجرا می‌شود.
    /// </summary>
    [global::Fratak_Shop.Web.Infrastructure.Team.RequireReauth]
    public async Task<IActionResult> OnPostAsync(Guid id, List<Guid> roles)
    {
        var ids = roles.Distinct().ToList();
        if (approvals.NeedsRoleApproval(await settingsService.GetAsync()))
            ShowResult(await approvals.RequestStaffRolesAsync(new global::Fratak_Shop.Core.Services.Team.StaffRolesPayload(id, ids, null), Access));
        else
            ShowResult(await access.SetUserRolesAsync(id, ids, Access));
        return Redirect($"/admin/staff/member/{id}");
    }

    [global::Fratak_Shop.Web.Infrastructure.Team.RequireReauth]
    public async Task<IActionResult> OnPostOwnerAsync(Guid id, bool owner)
    {
        if (approvals.NeedsRoleApproval(await settingsService.GetAsync()))
            ShowResult(await approvals.RequestStaffRolesAsync(new global::Fratak_Shop.Core.Services.Team.StaffRolesPayload(id, null, owner), Access));
        else
            ShowResult(await access.SetOwnerAsync(id, owner, Access));
        return Redirect($"/admin/staff/member/{id}");
    }

    /// <summary>
    /// بازنشانی برنامهٔ احراز هویت همکار (فاز ۱۰) – فقط مالک. کاربرد: گوشی کارمند گم شده و کد بازیابی هم ندارد.
    /// بعد از بازنشانی، ورود او دوباره با رمز/پیامک است تا برنامه را از نو راه‌اندازی کند.
    /// </summary>
    [AdminOwnerOnly]
    [global::Fratak_Shop.Web.Infrastructure.Team.RequireReauth]
    public async Task<IActionResult> OnPostResetTotpAsync(Guid id)
    {
        ShowResult(await totp.ResetAsync(id, Access));
        return Redirect($"/admin/staff/member/{id}");
    }
}
