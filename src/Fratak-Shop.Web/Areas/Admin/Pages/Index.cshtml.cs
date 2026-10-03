using Fratak_Shop.Core.DTOs.Dashboard.Query;
using Fratak_Shop.Core.Services.Dashboard;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Web.Infrastructure;

namespace Fratak_Shop.Web.Areas.Admin.Pages;

public class IndexModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IDashboardService dashboard)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public AdminDashboardDto Data { get; private set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "داشبورد";
        Data = await dashboard.GetAsync();
    }
}
