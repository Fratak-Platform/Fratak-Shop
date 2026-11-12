using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

/// <summary>
/// دور ۲۴ (پیشنهاد ۷۴): صفحهٔ «فنی ← گزارش امنیتی». امتیاز ساده (۰ تا ۱۰۰)، موارد نیازمند توجه با لینک رفع
/// (رمز پیش‌فرض کارکنان، ورود دومرحله‌ای، کد امنیتی، پشتیبان، حملهٔ حدس رمز، دسترسی غیرمجاز به پنل، دسترس‌پذیری)
/// و آمار ۷ روز اخیر. نتیجه ۱۰ دقیقه کش می‌شود؛ دکمهٔ «بررسی دوباره» (POST) گزارش تازه می‌سازد.
/// </summary>
[AdminPermission(AdminPermissions.AuditView, AdminPermissions.MonitoringView)]
public class SecurityReportModel(ISecurityReportService reports) : AdminPageModel
{
    /// <summary>گزارش ساخته‌شده.</summary>
    public SecurityReport Report { get; private set; } = null!;

    /// <summary>دور ۲۸ (#۱۱۹): گزارش‌های نقض CSP که مرورگرها فرستاده‌اند (پرتکرارترین اول).</summary>
    public IReadOnlyList<Infrastructure.Middleware.CspViolation> CspViolations { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Report = await reports.BuildAsync(DateTimeOffset.UtcNow, cancellationToken: HttpContext.RequestAborted);
        CspViolations = Infrastructure.Middleware.CspReports.Snapshot(30);
    }

    /// <summary>دور ۲۸: پاک کردن فهرست گزارش‌های CSP (بعد از بررسی).</summary>
    public IActionResult OnPostClearCsp()
    {
        Infrastructure.Middleware.CspReports.Clear();
        SuccessMessage = "فهرست گزارش‌های CSP پاک شد.";
        return RedirectToPage();
    }

    /// <summary>ساخت دوبارهٔ گزارش بدون کش (مثلاً بعد از عوض کردن رمز).</summary>
    public async Task<IActionResult> OnPostRefreshAsync()
    {
        await reports.BuildAsync(DateTimeOffset.UtcNow, refresh: true, HttpContext.RequestAborted);
        SuccessMessage = "گزارش امنیتی دوباره بررسی شد.";
        return RedirectToPage();
    }
}
