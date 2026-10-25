using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Staff;

[AdminPermission(AdminPermissions.StaffManage)]
public class RoleModel(IAdminAccessService access) : AdminPageModel
{
    public AdminRoleDto Role { get; private set; } = new() { Name = "" };
    public bool IsNew => Role.Id == Guid.Empty;

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        if (id is { } roleId)
        {
            var role = await access.GetRoleAsync(roleId);
            if (role is null)
                return NotFound();
            Role = role;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid? id, string? name, string? description, string? color, List<string> permissions)
    {
        var result = await access.SaveRoleAsync(new SaveAdminRoleCommand
        {
            Id = id,
            Name = name?.Trim() ?? "",
            Description = description,
            Color = color,
            Permissions = permissions.Where(p => AdminPermissions.All.Any(x => x.Key == p)).Distinct().ToList(),
        }, Access);

        if (Succeeded(result))
        {
            SuccessMessage = result.Message;
            return Redirect("/admin/staff");
        }

        ErrorMessage = result.Message;
        Role = new AdminRoleDto { Id = id ?? Guid.Empty, Name = name ?? "", Description = description, Color = color, Permissions = permissions };
        if (id is { } roleId && await access.GetRoleAsync(roleId) is { } existing)
        {
            Role.IsSystem = existing.IsSystem;
            Role.UserCount = existing.UserCount;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await access.DeleteRoleAsync(id, Access);
        ShowResult(result);
        return Succeeded(result) ? Redirect("/admin/staff") : Redirect($"/admin/staff/role/{id}");
    }
}
