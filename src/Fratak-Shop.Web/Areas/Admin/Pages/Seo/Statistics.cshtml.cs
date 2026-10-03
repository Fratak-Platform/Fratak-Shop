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
public class StatisticsModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    ISeoStatisticsService statistics)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<ContentVisitStat> TopContent { get; private set; } = [];

    public List<ContentVisitStat> UnvisitedContent { get; private set; } = [];

    public List<DailyVisitStat> DailyTrend { get; private set; } = [];

    public int TotalVisits { get; private set; }

    [BindProperty(SupportsGet = true)]
    public int Days { get; set; } = 30;

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "آمار بازدید و محتوا";
        TopContent = await statistics.GetTopContentAsync(Days, 10);
        UnvisitedContent = await statistics.GetUnvisitedContentAsync(Days, 10);
        DailyTrend = await statistics.GetDailyTrendAsync(14);
        TotalVisits = await statistics.GetTotalVisitsAsync(Days);
    }
}
