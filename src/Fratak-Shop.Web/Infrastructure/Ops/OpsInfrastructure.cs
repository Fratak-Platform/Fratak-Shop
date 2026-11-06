using System.Diagnostics;
using System.IO.Compression;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Ops;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Web.Infrastructure.Ops;

// ═══════════════════════════════════════════════════════════════════════════
//  لایهٔ Web بخش «فنی» (مرحلهٔ هفتم – فاز ۹)
//  • UploadsArchiver: پشتیبان zip از wwwroot/uploads در App_Data/backups.
//  • OpsMetricsSource: اندازه‌گیری شاخص‌های هشدار (درخواست‌ها، دیسک، حافظه، کارها، پایگاه داده).
//  • OpsWorker: هر دقیقه قانون‌های فایروال و کارهای متوقف را تازه می‌کند؛ هر ۵ دقیقه شمارنده‌ها را ذخیره،
//    IPهای مهاجم را مسدود و هشدارها را بررسی می‌کند؛ روزی یک بار فایل‌های گزارش قدیمی را پاک می‌کند.
//  • BackupWorker: هر ۵ دقیقه بررسی می‌کند نوبت پشتیبان رسیده یا نه (جدا از OpsWorker چون پشتیبان طولانی است).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// ساخت فایل zip از پوشهٔ آپلودها. فایل‌ها فقط در App_Data/backups ساخته و حذف می‌شوند (خارج از wwwroot تا از اینترنت
/// قابل دانلود نباشند). محل استفاده: BackupService (پشتیبان دستی/خودکار با گزینهٔ «پشتیبان فایل‌های آپلود»).
/// </summary>
public sealed class UploadsArchiver(IWebHostEnvironment environment, ILogger<UploadsArchiver> logger) : IUploadsArchiver
{
    public string UploadsFolder => Path.Combine(environment.WebRootPath, "uploads");

    public string BackupsFolder => Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "backups"));

    public bool IsAvailable => Directory.Exists(UploadsFolder);

    public async Task<(string? Path, long Size, string? Error)> CreateAsync(string fileName, CancellationToken cancellationToken)
    {
        if (fileName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || fileName.Contains("..") || !fileName.EndsWith(".zip", StringComparison.Ordinal))
            return (null, 0, "نام فایل معتبر نیست.");
        if (!IsAvailable)
            return (null, 0, "پوشهٔ آپلودها وجود ندارد.");

        Directory.CreateDirectory(BackupsFolder);
        var target = System.IO.Path.Combine(BackupsFolder, fileName);
        var temp = target + ".part";
        try
        {
            // فشرده‌سازی در رشتهٔ جدا تا درخواست‌ها کند نشوند. «سریع‌ترین» چون تصاویر از قبل فشرده‌اند.
            await Task.Run(() =>
            {
                if (File.Exists(temp))
                    File.Delete(temp);
                ZipFile.CreateFromDirectory(UploadsFolder, temp, CompressionLevel.Fastest, includeBaseDirectory: false);
            }, cancellationToken);
            File.Move(temp, target, overwrite: true);
            return (target, new FileInfo(target).Length, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Creating the uploads backup failed");
            TryDelete(temp);
            return (null, 0, "ساخت فایل zip ناموفق بود: " + exception.Message);
        }
    }

    public bool Delete(string path)
    {
        // فقط فایل‌های داخل پوشهٔ پشتیبان (جلوگیری از حذف فایل دیگری با مسیر دستکاری‌شده در دیتابیس).
        var full = System.IO.Path.GetFullPath(path);
        if (!full.StartsWith(BackupsFolder + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full))
            return false;
        return TryDelete(full);
    }

    /// <summary>آیا این مسیر داخل پوشهٔ پشتیبان آپلودهاست؟ (برای دانلود امن).</summary>
    public bool IsInsideBackups(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path).StartsWith(BackupsFolder + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>
/// اندازه‌گیری شاخص‌های هشدار. شاخص‌های دیتابیسی را OpsDbMetrics (لایهٔ Core) حساب می‌کند.
/// مثال: ServerErrorPercent = خطاهای ۵۰۰ تقسیم بر کل درخواست‌های ۵ دقیقهٔ اخیر × ۱۰۰ (با کمتر از ۲۰ درخواست null).
/// </summary>
public sealed class OpsMetricsSource(
    FratakShopDbContext context,
    IStoreSettingService settings,
    ActivityTracker tracker,
    HealthService health,
    IWebHostEnvironment environment) : IOpsMetricsSource
{
    /// <summary>با ترافیک خیلی کم درصدها بی‌معنی‌اند (۱ خطا از ۲ درخواست = ۵۰٪).</summary>
    private const int MinRequests = 20;

    public async Task<double?> MeasureAsync(AlertMetric metric, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        switch (metric)
        {
            case AlertMetric.ServerErrorPercent:
            {
                var minutes = tracker.Minutes(5);
                var requests = minutes.Sum(m => m.Requests);
                return requests < MinRequests ? null : minutes.Sum(m => m.ServerErrors) * 100d / requests;
            }
            case AlertMetric.AverageResponseMs:
            {
                var minutes = tracker.Minutes(5);
                var requests = minutes.Sum(m => m.Requests);
                return requests < MinRequests ? null : minutes.Sum(m => m.AverageMs * m.Requests) / requests;
            }
            case AlertMetric.DiskFreePercent:
            {
                var root = Path.GetPathRoot(Path.GetFullPath(environment.ContentRootPath));
                if (string.IsNullOrEmpty(root))
                    return null;
                var drive = new DriveInfo(root);
                return drive.TotalSize == 0 ? null : drive.AvailableFreeSpace * 100d / drive.TotalSize;
            }
            case AlertMetric.DatabaseLatencyMs:
            {
                var (ok, ms) = await health.PingDatabaseAsync(cancellationToken);
                // قطع کامل = بدترین مقدار ممکن تا حتماً هشدار برود.
                return ok ? ms : 60_000;
            }
            case AlertMetric.FailingJobs:
                return JobMonitor.CountUnhealthy(now);
            case AlertMetric.MemoryMb:
            {
                using var process = Process.GetCurrentProcess();
                return process.WorkingSet64 / 1024d / 1024d;
            }
            default:
            {
                var s = await settings.GetAsync(cancellationToken);
                return await OpsDbMetrics.MeasureAsync(context, metric, s.IsBackupEnabled, now, cancellationToken);
            }
        }
    }
}

/// <summary>
/// سرویس پس‌زمینهٔ «فنی». خودش قابل توقف نیست (فایروال باید همیشه به‌روز باشد) ولی هشدارها جداگانه قابل توقف‌اند.
/// </summary>
public sealed class OpsWorker(IServiceScopeFactory scopeFactory, LogStore logs, ILogger<OpsWorker> logger) : BackgroundService
{
    public const string FirewallJobName = "فایروال و مسدودسازی خودکار IP";
    public const string AlertsJobName = "هشدارهای فنی";
    public const string LogsJobName = "پاک‌سازی فایل‌های گزارش";

    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SlowEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DailyEvery = TimeSpan.FromHours(24);

    private DateTimeOffset _lastSlow = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDaily = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobMonitor.Register(FirewallJobName, TimeSpan.FromMinutes(10), pausable: false);
        JobMonitor.Register(AlertsJobName, TimeSpan.FromMinutes(30));
        JobMonitor.Register(LogsJobName, TimeSpan.FromDays(2));

        try
        {
            // کمی صبر تا راه‌اندازی سایت (مایگریشن و seed) تمام شود؛ قانون‌ها در Program.cs همان ابتدا بارگذاری شده‌اند.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            using var timer = new PeriodicTimer(Tick);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var now = DateTimeOffset.UtcNow;
        var slow = now - _lastSlow >= SlowEvery;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var settings = await services.GetRequiredService<IStoreSettingService>().GetAsync(stoppingToken);

            // ۱) کارهای متوقف (اگر از سرور دیگری تغییر کرده باشد) و قانون‌های فایروال.
            JobMonitor.SetPaused(await services.GetRequiredService<IBackgroundJobControl>().GetPausedAsync(stoppingToken));
            await JobMonitor.TrackAsync(FirewallJobName, async () =>
            {
                var firewall = services.GetRequiredService<IFirewallService>();
                if (!slow)
                {
                    await firewall.ReloadAsync(stoppingToken);
                    return 0;
                }
                // هر ۵ دقیقه: ذخیرهٔ شمارنده‌ها و حذف منقضی‌ها، سپس مسدودسازی خودکار.
                var removed = await firewall.MaintainAsync(now, stoppingToken);
                var blocked = settings.IsIpAutoBlockEnabled
                    ? await firewall.AutoBlockAsync(settings.IpAutoBlockThreshold, settings.IpAutoBlockHours, now, stoppingToken)
                    : 0;
                if (blocked > 0)
                {
                    await services.GetRequiredService<global::Fratak_Shop.Core.Services.Admin.IStaffNotificationService>().NotifyAsync(new global::Fratak_Shop.Core.Services.Admin.StaffAlert(
                        global::Fratak_Shop.Data.Entities.Admin.StaffNotificationKind.Alert, "مسدودسازی خودکار IP",
                        $"{PersianNumbers.Digits(blocked)} IP به‌خاطر ورودهای ناموفق پیاپی برای {PersianNumbers.Digits(settings.IpAutoBlockHours)} ساعت مسدود شد.",
                        "/admin/system/firewall", [global::Fratak_Shop.Core.Services.Admin.AdminPermissions.SystemManage]));
                }
                return removed + blocked;
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Firewall refresh failed");
        }

        if (slow)
        {
            _lastSlow = now;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var alerts = scope.ServiceProvider.GetRequiredService<IAlertService>();
                var metrics = scope.ServiceProvider.GetRequiredService<IOpsMetricsSource>();
                var sent = await JobMonitor.TrackAsync(AlertsJobName, () => alerts.EvaluateAsync(metrics, now, stoppingToken));
                if (sent > 0)
                    logger.LogWarning("Ops alerts: {Count} alert notification(s) sent", sent);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Evaluating ops alerts failed");
            }
        }

        if (now - _lastDaily >= DailyEvery)
        {
            _lastDaily = now;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var settings = await scope.ServiceProvider.GetRequiredService<IStoreSettingService>().GetAsync(stoppingToken);
                await JobMonitor.TrackAsync(LogsJobName, () => Task.FromResult(logs.Prune(settings.LogFileRetentionDays, now)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Pruning log files failed");
            }
        }
    }
}

/// <summary>
/// پشتیبان‌گیری خودکار طبق تنظیمات (تعداد در روز و ساعت شروع). هر ۵ دقیقه نوبت فعلی را بررسی می‌کند؛ اجرای تکراری یک نوبت
/// با کلید یکتای نوبت در دیتابیس جلوگیری می‌شود (حتی با چند سرور).
/// </summary>
public sealed class BackupWorker(IServiceScopeFactory scopeFactory, ILogger<BackupWorker> logger) : BackgroundService
{
    public const string JobName = "پشتیبان‌گیری خودکار";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobMonitor.Register(JobName, TimeSpan.FromHours(25));
        try
        {
            // ۳ دقیقه پس از راه‌اندازی تا سایت سرپا شود و پشتیبان با بالا آمدن سایت هم‌زمان نشود.
            await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var backups = scope.ServiceProvider.GetRequiredService<IBackupService>();
                    var settings = await scope.ServiceProvider.GetRequiredService<IStoreSettingService>().GetAsync(stoppingToken);
                    if (!settings.IsBackupEnabled || JobMonitor.ShouldSkip(JobName))
                        continue;
                    // بررسی ارزان پیش از ثبت «اجرا» در مانیتور کارها: نوبتی که انجام شده دوباره شمرده نمی‌شود.
                    var (slot, _) = BackupSchedule.CurrentSlot(DateTimeOffset.UtcNow, settings.BackupRunsPerDay, settings.BackupStartHour);
                    if (await backups.HasSlotAsync(slot, stoppingToken))
                        continue;
                    var result = await JobMonitor.TrackAsync(JobName, () => backups.RunDueAsync(DateTimeOffset.UtcNow, stoppingToken));
                    if (result is { Ran: true })
                        logger.Log(result.Ok ? LogLevel.Information : LogLevel.Error, "Scheduled backup: {Message}", result.Message);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Scheduled backup failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
