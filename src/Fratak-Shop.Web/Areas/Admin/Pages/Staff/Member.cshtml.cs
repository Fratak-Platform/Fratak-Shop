using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Staff;

[AdminPermission(AdminPermissions.StaffManage)]
public class MemberModel(IAdminAccessService access, IAdminDataService data) : AdminPageModel
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

    public async Task<IActionResult> OnPostAsync(Guid id, List<Guid> roles)
    {
        ShowResult(await access.SetUserRolesAsync(id, roles.Distinct().ToList(), Access));
        return Redirect($"/admin/staff/member/{id}");
    }

    public async Task<IActionResult> OnPostOwnerAsync(Guid id, bool owner)
    {
        ShowResult(await access.SetOwnerAsync(id, owner, Access));
        return Redirect($"/admin/staff/member/{id}");
    }
}
