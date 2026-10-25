using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Staff;

[AdminPermission(AdminPermissions.StaffManage)]
public class IndexModel(IAdminAccessService access, IAdminDataService data) : AdminPageModel
{
    public List<StaffMemberDto> Staff { get; private set; } = [];
    public List<AdminRoleDto> Roles { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Staff = await access.GetStaffAsync();
        Roles = await access.GetRolesAsync();
    }

    /// <summary>Find a registered user by mobile number to give them a role.</summary>
    public async Task<IActionResult> OnPostFindAsync(string? phone)
    {
        var number = SearchText.NormalizeDigits(phone?.Trim() ?? "");
        var id = await data.Query<User>().Where(u => u.PhoneNumber == number).Select(u => (Guid?)u.Id).FirstOrDefaultAsync();
        if (id is null)
        {
            ErrorMessage = "کاربری با این شماره ثبت‌نام نکرده است. ابتدا باید در سایت ثبت‌نام کند.";
            return Redirect("/admin/staff");
        }
        return Redirect($"/admin/staff/member/{id}");
    }
}
