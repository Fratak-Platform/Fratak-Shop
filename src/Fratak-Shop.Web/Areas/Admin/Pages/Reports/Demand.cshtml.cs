using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Reports;

/// <summary>
/// صفحهٔ «تقاضای مشتریان» (/admin/reports/demand، مرحلهٔ هفتم – فاز ۷): چه چیزی می‌خواهند که نداریم یا نمی‌خرند؟
/// جست‌وجوهای بی‌نتیجه، پرتکرارترین جست‌وجوها، درخواست‌های «موجود شد خبرم کن»، محصولات ناموجودِ پربازدید
/// (با فروش ازدست‌رفتهٔ تخمینی) و محصولات پربازدید بی‌فروش. برای تصمیم خرید از تأمین‌کننده و افزودن کالای تازه.
/// </summary>
[AdminPermission(AdminPermissions.ReportsView, AdminPermissions.FinanceView, AdminPermissions.MarketingManage, AdminPermissions.ProductsManage)]
public class DemandModel(IAnalyticsReportService reports) : ReportPageModel
{
    public override string BasePath => "/admin/reports/demand";

    public DemandReport Report { get; private set; } = new();

    public async Task OnGetAsync()
    {
        ResolvePeriod();
        Report = await reports.GetDemandAsync(Period.Current, HttpContext.RequestAborted);
    }
}
