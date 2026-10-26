using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Data.Entities.Ops;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

/// <summary>
/// صفحهٔ «فنی ← کارهای پس‌زمینه» (مرحلهٔ هفتم – فاز ۹): وضعیت هر کار زمان‌بندی‌شده، توقف/ادامه و اجرای فوری.
/// مثال کاربرد: هنگام جابه‌جایی سرور یا قطعی سایت تأمین‌کننده، «بررسی منبع تأمین‌کننده» را متوقف کنید.
/// </summary>
[AdminPermission(AdminPermissions.MonitoringView, AdminPermissions.SystemManage)]
public class JobsModel(IBackgroundJobControl control, IMonitoringService monitoring) : AdminPageModel
{
    public IReadOnlyList<JobState> Jobs { get; private set; } = [];
    public Dictionary<string, BackgroundJobSetting> Settings { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var rows = await control.ListAsync(HttpContext.RequestAborted);
        // اگر سرور دیگری تغییر داده باشد، همین الان هماهنگ شود.
        JobMonitor.SetPaused(rows.Where(r => r.IsPaused).Select(r => r.JobName));
        Settings = rows.GroupBy(r => r.JobName).ToDictionary(g => g.Key, g => g.First());
        Jobs = JobMonitor.All;
    }

    /// <summary>وضعیت نمایشی یک کار: (متن، کلاس نشان).</summary>
    public static (string Text, string Css) Status(JobState job)
    {
        if (JobMonitor.IsPaused(job.Name))
            return ("متوقف", "a-badge-yellow");
        if (job.IsFailing)
            return ("ناموفق", "a-badge-red");
        if (job.LastStartedAt is null)
            return ("هنوز اجرا نشده", "a-badge-gray");
        if (job.LastSucceededAt is { } ok && DateTimeOffset.UtcNow - ok > job.ExpectedInterval * 3)
            return ("دیر شده", "a-badge-yellow");
        return ("سالم", "a-badge-green");
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostPauseAsync(string name, bool paused)
    {
        var job = JobMonitor.Find(name);
        if (job is null)
        {
            ErrorMessage = "این کار پیدا نشد.";
            return Redirect("/admin/system/jobs");
        }
        if (paused && !job.IsPausable)
        {
            ErrorMessage = "این کار حیاتی است و توقف آن باعث از دست رفتن اعلان‌ها یا آمار می‌شود.";
            return Redirect("/admin/system/jobs");
        }
        var result = await control.SetPausedAsync(job.Name, paused, ActorName, HttpContext.RequestAborted);
        if (Succeeded(result))
        {
            JobMonitor.SetPaused(await control.GetPausedAsync(HttpContext.RequestAborted));
            await monitoring.LogActionAsync(paused ? "JobPause" : "JobResume", "System", null, (paused ? "توقف کار: " : "ادامهٔ کار: ") + job.Name);
        }
        ShowResult(result);
        return Redirect("/admin/system/jobs");
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostRunAsync(string name)
    {
        var job = JobMonitor.Find(name);
        if (job?.RunNow is null)
        {
            ErrorMessage = "این کار اجرای فوری ندارد؛ در نوبت بعدی خودکار اجرا می‌شود.";
            return Redirect("/admin/system/jobs");
        }
        if (JobMonitor.IsPaused(job.Name))
        {
            ErrorMessage = "این کار متوقف است؛ ابتدا «ادامه» را بزنید.";
            return Redirect("/admin/system/jobs");
        }
        if (job.RunNow(ActorName))
        {
            await monitoring.LogActionAsync("JobRunNow", "System", null, "اجرای فوری کار: " + job.Name);
            SuccessMessage = $"«{job.Name}» در صف اجرا قرار گرفت؛ چند لحظهٔ دیگر صفحه را تازه کنید.";
        }
        else
        {
            ErrorMessage = "این کار همین الان در صف است.";
        }
        return Redirect("/admin/system/jobs");
    }
}
