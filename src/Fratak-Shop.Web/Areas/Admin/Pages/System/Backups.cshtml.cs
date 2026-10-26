using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Data.Entities.Ops;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Ops;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

/// <summary>
/// صفحهٔ «فنی ← پشتیبان‌گیری» (مرحلهٔ هفتم – فاز ۹): وضعیت پشتیبان خودکار، اجرای دستی، فهرست نسخه‌ها، دانلود و حذف.
/// دیدن: «مانیتورینگ»؛ اجرا/حذف/دانلود: «عملیات فنی» (فایل پشتیبان همهٔ اطلاعات مشتریان را دارد).
/// </summary>
[AdminPermission(AdminPermissions.MonitoringView, AdminPermissions.SystemManage)]
public class BackupsModel(IBackupService backups, IMonitoringService monitoring, UploadsArchiver archiver) : AdminPageModel
{
    public BackupOverview Overview { get; private set; } = null!;
    public List<BackupRecord> Records { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Overview = await backups.GetOverviewAsync(ct);
        Records = await backups.ListAsync(100, ct);
    }

    /// <summary>آیا فایل روی همین سرور است و می‌توان دانلودش کرد؟ (پشتیبان پایگاه داده روی سرور SQL جدا قابل دانلود نیست).</summary>
    public static bool CanDownload(BackupRecord record) =>
        record.Status == BackupStatus.Succeeded && record.IsLocal && record.FilePath is not null && global::System.IO.File.Exists(record.FilePath);

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostRunAsync(bool includeUploads)
    {
        var result = await backups.RunNowAsync(ActorName, includeUploads, HttpContext.RequestAborted);
        if (result.Ran)
            await monitoring.LogActionAsync("BackupRun", "System", null, "پشتیبان‌گیری دستی: " + result.Message);
        if (result.Ok && result.Ran)
            SuccessMessage = result.Message;
        else
            ErrorMessage = result.Message;
        return Redirect("/admin/system/backups");
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await backups.DeleteAsync(id, HttpContext.RequestAborted);
        if (Succeeded(result))
            await monitoring.LogActionAsync("BackupDelete", "System", id, "حذف نسخهٔ پشتیبان");
        ShowResult(result);
        return Redirect("/admin/system/backups");
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnGetDownloadAsync(Guid id)
    {
        var record = await backups.GetAsync(id, HttpContext.RequestAborted);
        if (record is null || !CanDownload(record))
        {
            ErrorMessage = "این فایل روی همین سرور در دسترس نیست (پشتیبان پایگاه داده روی سرور SQL Server ذخیره شده است).";
            return Redirect("/admin/system/backups");
        }
        // فقط پسوندهای پشتیبان؛ فایل آپلودها فقط از پوشهٔ مخصوص خودش.
        var path = record.FilePath!;
        var extension = global::System.IO.Path.GetExtension(path).ToLowerInvariant();
        if ((record.Kind == BackupKind.Database && extension != ".bak") || (record.Kind == BackupKind.Uploads && (extension != ".zip" || !archiver.IsInsideBackups(path))))
            return NotFound();
        await monitoring.LogActionAsync("BackupDownload", "System", id, "دانلود نسخهٔ پشتیبان " + global::System.IO.Path.GetFileName(path));
        return PhysicalFile(path, "application/octet-stream", global::System.IO.Path.GetFileName(path), enableRangeProcessing: true);
    }
}
