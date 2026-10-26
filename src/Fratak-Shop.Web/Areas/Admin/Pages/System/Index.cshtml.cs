using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.PageCache;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

[AdminPermission(AdminPermissions.MonitoringView)]
public class IndexModel(HealthService health, IMonitoringService monitoring, ActivityTracker tracker, ErrorLogQueue errorQueue, IMemoryCache memory) : AdminPageModel
{
    public List<HealthCheck> Checks { get; private set; } = [];
    public RuntimeInfo Runtime { get; private set; } = null!;
    public DatabaseInfoDto Database { get; private set; } = new();
    public ErrorStatsDto Errors { get; private set; } = new();
    public IReadOnlyList<MinuteStat> Minutes { get; private set; } = [];
    public IReadOnlyList<SlowRequest> Slow { get; private set; } = [];
    public IReadOnlyList<FailedRequest> Failed { get; private set; } = [];
    public IReadOnlyList<JobState> Jobs { get; private set; } = [];
    public int Online { get; private set; }
    public long TotalRequests { get; private set; }
    public long ErrorsCaptured { get; private set; }
    public long ErrorsDropped { get; private set; }
    /// <summary>آمار کش حافظه (تنظیمات، منوها، دسترسی‌ها…) – فاز ۹. null اگر آمارگیری فعال نباشد.</summary>
    public MemoryCacheStatistics? MemoryStats { get; private set; }

    public HealthLevel Overall => Checks.Count == 0 ? HealthLevel.Healthy : Checks.Max(c => c.Level);

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Checks = await health.RunAsync(ct);
        Runtime = health.Runtime();
        Database = await monitoring.GetDatabaseInfoAsync(ct);
        Errors = await monitoring.GetErrorStatsAsync();
        Minutes = tracker.Minutes(60);
        Slow = tracker.SlowRequests().Take(15).ToList();
        Failed = tracker.FailedRequests().Take(15).ToList();
        Jobs = JobMonitor.All;
        Online = tracker.OnlineCount();
        TotalRequests = tracker.TotalRequests;
        ErrorsCaptured = errorQueue.Captured;
        ErrorsDropped = errorQueue.Dropped;
        MemoryStats = memory.GetCurrentStatistics();
    }

    /// <summary>درصد برخورد کش. مثال: (۹۰، ۱۰) → ۹۰٪؛ بدون درخواست → null.</summary>
    public static double? HitRate(long hits, long misses) => hits + misses == 0 ? null : hits * 100d / (hits + misses);

    /// <summary>Empties the public page cache and in-memory caches (settings, menus, permissions are reloaded on next use).</summary>
    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostClearCacheAsync([FromServices] IOutputCacheStore pageCache, [FromServices] IMemoryCache memoryCache)
    {
        await pageCache.EvictByTagAsync(PageCachePolicy.Tag, HttpContext.RequestAborted);
        PageCachePolicy.CountEviction();
        if (memoryCache is MemoryCache concrete)
            concrete.Compact(1.0);
        AdminAccessService.InvalidateAll();
        await monitoring.LogActionAsync("ClearCache", "System", null, "پاک‌سازی کش سایت");
        SuccessMessage = "کش صفحات و حافظه پاک شد.";
        return Redirect("/admin/system");
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostPurgeAsync(int errorDays = 30, int auditDays = 180)
    {
        errorDays = Math.Clamp(errorDays, 1, 3650);
        auditDays = Math.Clamp(auditDays, 30, 3650);
        var removed = await monitoring.PurgeAsync(errorDays, auditDays, HttpContext.RequestAborted);
        await monitoring.LogActionAsync("PurgeLogs", "System", null, $"حذف لاگ‌های قدیمی (خطا: {errorDays} روز، فعالیت: {auditDays} روز) – {removed} ردیف");
        SuccessMessage = $"{removed:N0} ردیف لاگ قدیمی حذف شد.";
        return Redirect("/admin/system");
    }
}
