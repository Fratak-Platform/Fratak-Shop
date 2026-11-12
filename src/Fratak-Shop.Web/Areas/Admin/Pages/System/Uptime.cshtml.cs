using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

/// <summary>
/// دور ۲۴ (پیشنهاد ۷۳): صفحهٔ «فنی ← دسترس‌پذیری». درصد در دسترس بودن سایت در ۲۴ ساعت / ۷ روز / ۳۰ روز،
/// جمع دقیقه‌های قطعی و فهرست قطعی‌ها (توقف ناگهانی برنامه یا قطع پایگاه داده) از پایشگر داخلی UptimeMonitor.
/// فقط خواندنی است؛ GET چیزی را تغییر نمی‌دهد.
/// </summary>
[AdminPermission(AdminPermissions.MonitoringView)]
public class UptimeModel(IUptimeService uptime) : AdminPageModel
{
    /// <summary>خلاصهٔ دسترس‌پذیری.</summary>
    public UptimeSummary Summary { get; private set; } = new(100, 100, 100, 0, 0, []);

    public async Task OnGetAsync() => Summary = await uptime.SummaryAsync(DateTimeOffset.UtcNow, HttpContext.RequestAborted);
}
