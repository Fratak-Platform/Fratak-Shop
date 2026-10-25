using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Web.Areas.Admin.Pages;

public class IndexModel(IAdminReportService reports, ActivityTracker tracker) : AdminPageModel
{
    public DashboardDto Data { get; private set; } = new();

    public int OnlineNow { get; private set; }

    public bool ShowSales => CanAny(AdminPermissions.FinanceView, AdminPermissions.OrdersView);

    public async Task OnGetAsync()
    {
        Data = await reports.GetDashboardAsync(HttpContext.RequestAborted);
        OnlineNow = tracker.OnlineCount();
    }
}
