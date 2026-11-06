using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Reports;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages;

/// <summary>
/// داشبورد پنل. از مرحلهٔ هفتم – فاز ۷ب شخصی‌سازی می‌شود: هر کارمند ترتیب و نمایش ویجت‌ها را
/// (کشیدن و رها کردن یا دکمهٔ بالا/پایین) انتخاب می‌کند؛ تعریف ویجت‌ها: Infrastructure/Admin/DashboardLayout.
/// داده‌ی هر ویجت فقط وقتی خوانده می‌شود که آن ویجت نمایش داده شود.
/// </summary>
public class IndexModel(
    IAdminReportService reports,
    ActivityTracker tracker,
    IAdminPreferenceService preferences,
    ISalesGoalService goals,
    IAnalyticsReportService analytics,
    IExpenseService expenses) : AdminPageModel
{
    public DashboardDto Data { get; private set; } = new();

    public int OnlineNow { get; private set; }

    public bool ShowSales => CanAny(AdminPermissions.FinanceView, AdminPermissions.OrdersView);

    /// <summary>همهٔ ویجت‌های در دسترس به ترتیب کاربر (پنهان‌ها هم برای فرم شخصی‌سازی).</summary>
    public List<ArrangedWidget> Layout { get; private set; } = [];

    public GoalProgress? Goal { get; private set; }
    public FunnelReport? VisitorsToday { get; private set; }
    public List<ExpenseRow> UpcomingExpenses { get; private set; } = [];

    public bool IsVisible(string key) => Layout.Any(w => w.Visible && w.Widget.Key == key);

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        var preference = await preferences.GetAsync<DashboardLayoutPreference>(ActorId, DashboardLayout.PreferenceKey);
        Layout = DashboardLayout.Arrange(preference, w => w.IsAvailable(Access));

        Data = await reports.GetDashboardAsync(ct);
        OnlineNow = tracker.OnlineCount();

        if (IsVisible("goal"))
            Goal = await goals.GetProgressAsync(cancellationToken: ct);
        if (IsVisible("visitors-today"))
        {
            var today = IranClock.Today(DateTimeOffset.UtcNow);
            VisitorsToday = await analytics.GetFunnelAsync(new ReportPeriod(IranClock.StartOfDay(today), null), ct);
        }
        if (IsVisible("expenses-due"))
            UpcomingExpenses = await expenses.GetUpcomingAsync(5, ct);
    }

    /// <summary>ذخیرهٔ چیدمان: order = همهٔ کلیدها به ترتیب فهرست، visible = تیک‌خورده‌ها.</summary>
    public async Task<IActionResult> OnPostLayoutAsync(List<string>? order, List<string>? visible)
    {
        var layout = DashboardLayout.FromForm(order, visible);
        var saved = await preferences.SetAsync(ActorId, DashboardLayout.PreferenceKey, layout);
        if (saved)
            SuccessMessage = "چیدمان داشبورد ذخیره شد.";
        else
            ErrorMessage = "ذخیرهٔ چیدمان انجام نشد؛ دوباره تلاش کنید.";
        return LocalRedirect("/admin");
    }

    public async Task<IActionResult> OnPostResetLayoutAsync()
    {
        await preferences.RemoveAsync(ActorId, DashboardLayout.PreferenceKey);
        SuccessMessage = "چیدمان پیش‌فرض داشبورد برگردانده شد.";
        return LocalRedirect("/admin");
    }
}
