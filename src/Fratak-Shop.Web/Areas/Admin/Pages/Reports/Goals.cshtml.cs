using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Reports;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Reports;

/// <summary>
/// صفحهٔ «هدف فروش ماهانه» (/admin/reports/goals، مرحلهٔ هفتم – فاز ۷ب).
/// <para>
/// تعیین هدف مبلغ (و اختیاری تعداد سفارش) برای هر ماه شمسی، نوار پیشرفت، پیش‌بینی پایان ماه با سرعت فعلی،
/// «فروش لازم در هر روز باقی‌مانده» و تاریخچهٔ ۱۲ ماه اخیر (هدف در برابر واقعی).
/// </para>
/// دسترسی: مشاهده با «گزارش‌ها» یا «گزارش مالی»؛ تعیین هدف با «هزینه‌ها و هدف فروش».
/// منطق: Core/Services/Insights/SalesGoalService (همان قاعدهٔ فروش قطعی گزارش مالی).
/// </summary>
[AdminPermission(AdminPermissions.ReportsView, AdminPermissions.FinanceView, AdminPermissions.ExpensesManage)]
public class GoalsModel(ISalesGoalService goals, IAdminReportService adminReports) : AdminPageModel
{
    /// <summary>سال شمسی ماه انتخاب‌شده (خالی = ماه جاری).</summary>
    [BindProperty(SupportsGet = true, Name = "y")] public int? Year { get; set; }

    [BindProperty(SupportsGet = true, Name = "m")] public int? Month { get; set; }

    public GoalProgress Progress { get; private set; } = new();
    public List<GoalProgress> History { get; private set; } = [];

    /// <summary>فروش روزانهٔ ماه انتخاب‌شده (برای نمودار).</summary>
    public List<ChartPoint> Daily { get; private set; } = [];

    /// <summary>پیشنهاد هدف: میانگین فروش سه ماه کامل قبل + ۱۰٪ (گرد به میلیون).</summary>
    public decimal SuggestedTarget { get; private set; }

    public bool CanSet => Can(AdminPermissions.ExpensesManage);

    public (int Year, int Month) Current { get; private set; }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Current = JalaliMonth.Of(IranClock.Today(DateTimeOffset.UtcNow));
        Progress = await goals.GetProgressAsync(Year, Month, ct);
        History = await goals.GetHistoryAsync(12, ct);

        var (start, end) = JalaliMonth.Range(Progress.Year, Progress.Month);
        if (start <= IranClock.Today(DateTimeOffset.UtcNow))
            Daily = (await adminReports.GetSalesAsync(new ReportPeriod(IranClock.StartOfDay(start), IranClock.StartOfDay(end)), ct)).Daily;

        var lastThree = History.Skip(1).Take(3).ToList();
        if (lastThree.Count > 0)
            SuggestedTarget = Math.Ceiling(lastThree.Average(h => h.Achieved) * 1.1m / 1_000_000m) * 1_000_000m;
    }

    [AdminPermission(AdminPermissions.ExpensesManage)]
    public async Task<IActionResult> OnPostSaveAsync(int year, int month, decimal targetAmount, int? targetOrders, string? note)
    {
        ShowResult(await goals.SaveAsync(new SalesGoalInput(year, month, targetAmount, targetOrders is > 0 ? targetOrders : null, note), ActorName, HttpContext.RequestAborted));
        return LocalRedirect($"/admin/reports/goals?y={year}&m={month}");
    }

    [AdminPermission(AdminPermissions.ExpensesManage)]
    public async Task<IActionResult> OnPostDeleteAsync(int year, int month)
    {
        ShowResult(await goals.DeleteAsync(year, month, HttpContext.RequestAborted));
        return LocalRedirect($"/admin/reports/goals?y={year}&m={month}");
    }

    /// <summary>کلاس رنگ وضعیت پیش‌بینی برای نشان.</summary>
    public static string StatusBadge(string status) => status switch
    {
        "done" => "a-badge-green",
        "ahead" => "a-badge-green",
        "on-track" => "a-badge-blue",
        "behind" => "a-badge-red",
        _ => "a-badge-gray",
    };
}
