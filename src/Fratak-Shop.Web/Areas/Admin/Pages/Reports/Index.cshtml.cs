using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Reports;

/// <summary>
/// صفحهٔ «قیف فروش و بازدید» (/admin/reports، مرحلهٔ هفتم – فاز ۷):
/// بازدیدکننده‌های یکتا، پله‌های قیف (بازدید → محصول → سبد → تسویه → سفارش → خرید قطعی)، نمودار روزانه،
/// منبع‌های ورودی برتر، دستگاه‌ها، صفحه‌های ورود و نقشهٔ حرارتی روز × ساعت.
/// منطق: Core/Services/Reports/AnalyticsReportService.cs ؛ جمع‌آوری داده: Web/Infrastructure/Analytics.
/// </summary>
[AdminPermission(AdminPermissions.ReportsView, AdminPermissions.FinanceView, AdminPermissions.MarketingManage)]
public class IndexModel(IAnalyticsReportService reports, IStoreSettingService settings) : ReportPageModel
{
    public override string BasePath => "/admin/reports";

    public FunnelReport Funnel { get; private set; } = new();
    public HeatmapReport Heatmap { get; private set; } = new();

    /// <summary>ثبت آمار در تنظیمات خاموش است (داده‌های قبلی نمایش داده می‌شوند).</summary>
    public bool TrackingDisabled { get; private set; }

    /// <summary>زبانهٔ نقشهٔ حرارتی: visits (بازدید) یا orders (سفارش).</summary>
    public string HeatTab { get; private set; } = "visits";

    public async Task OnGetAsync(string? heat = null)
    {
        ResolvePeriod();
        HeatTab = heat == "orders" ? "orders" : "visits";
        TrackingDisabled = !(await settings.GetAsync(HttpContext.RequestAborted)).IsAnalyticsEnabled;
        Funnel = await reports.GetFunnelAsync(Period.Current, HttpContext.RequestAborted);
        Heatmap = await reports.GetHeatmapAsync(Period.Current, HttpContext.RequestAborted);
    }
}
