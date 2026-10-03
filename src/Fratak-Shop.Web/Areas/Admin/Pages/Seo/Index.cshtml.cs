using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

[Authorize(Policy = PolicyNames.Seo)]
public class IndexModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public SeoDashboardDto Dashboard { get; private set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "مرکز سئو";
        Dashboard = await seoAudits.GetDashboardAsync();
    }

    /// <summary>اجرای دسته‌جمعی سئوی هوشمند روی محتوای بدون سئو.</summary>
    public async Task<IActionResult> OnPostRunBatchAsync(SeoEntityType entityType, int take = 5)
    {
        var result = await seoAudits.GenerateForMissingAsync(entityType, take, CurrentUserId);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Seo/Index", new { area = "Admin" });
    }
}
