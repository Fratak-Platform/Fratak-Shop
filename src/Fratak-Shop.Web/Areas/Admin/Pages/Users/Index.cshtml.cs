using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Users;

[Authorize(Policy = PolicyNames.AdminOnly)]
public class IndexModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IUserService users)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public UserFilterResult Result { get; private set; } = null!;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "active")]
    public bool? IsActive { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageId { get; set; } = 1;

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "کاربران";

        Result = await users.GetFilterAsync(new UserFilterParams
        {
            Search = Search,
            IsActive = IsActive,
            PageId = PageId,
            Take = 15,
        });
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        var user = await users.GetUserDtoAsync(id);
        if (user is null)
        {
            Notify("کاربر یافت نشد.", "error");
            return RedirectToPage("/Users/Index", new { area = "Admin" });
        }

        var result = await users.SetActiveAsync(id, !user.IsActive);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Users/Index", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await users.DeleteUserAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Users/Index", new { area = "Admin" });
    }
}
