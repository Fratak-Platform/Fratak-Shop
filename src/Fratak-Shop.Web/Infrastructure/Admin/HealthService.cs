using System.Diagnostics;
using System.Runtime;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.Admin;

public enum HealthLevel
{
    Healthy = 0,
    Warning = 1,
    Critical = 2,
}

public sealed record HealthCheck(string Name, HealthLevel Level, string Summary, string? Hint = null);

public sealed record RuntimeInfo(
    string Environment, string Framework, string OS, string Machine, int Processors, DateTimeOffset StartedAt, TimeSpan Uptime,
    double WorkingSetMb, double ManagedHeapMb, int Gen0, int Gen1, int Gen2, double CpuSeconds, int Threads,
    int ThreadPoolAvailable, long PendingWorkItems, string GcMode, string Version);

/// <summary>Health checks of the site (database, migrations, disk, jobs, error rate, configuration) for the admin panel and <c>/health</c>.</summary>
public sealed class HealthService(
    IServiceScopeFactory scopeFactory,
    IWebHostEnvironment environment,
    ActivityTracker tracker,
    ErrorLogQueue errorQueue,
    ISmsSender smsSender)
{
    private static readonly DateTimeOffset ProcessStarted = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    /// <summary>Database reachability only – cheap enough for an uptime monitor.</summary>
    public async Task<(bool Ok, double Milliseconds)> PingDatabaseAsync(CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var ok = await context.Database.CanConnectAsync(cancellationToken);
            return (ok, watch.Elapsed.TotalMilliseconds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (false, watch.Elapsed.TotalMilliseconds);
        }
    }

    public async Task<List<HealthCheck>> RunAsync(CancellationToken cancellationToken)
    {
        var checks = new List<HealthCheck>();

        var (dbOk, dbMs) = await PingDatabaseAsync(cancellationToken);
        checks.Add(!dbOk
            ? new HealthCheck("پایگاه داده", HealthLevel.Critical, "اتصال به پایگاه داده برقرار نشد.", "رشته اتصال و در دسترس بودن SQL Server را بررسی کنید.")
            : dbMs > 500
                ? new HealthCheck("پایگاه داده", HealthLevel.Warning, $"پاسخ کند ({dbMs:0} میلی‌ثانیه).", "بار سرور پایگاه داده را بررسی کنید.")
                : new HealthCheck("پایگاه داده", HealthLevel.Healthy, $"در دسترس ({dbMs:0} میلی‌ثانیه)."));

        if (dbOk)
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            try
            {
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                checks.Add(pending.Count == 0
                    ? new HealthCheck("مایگریشن‌ها", HealthLevel.Healthy, "ساختار پایگاه داده به‌روز است.")
                    : new HealthCheck("مایگریشن‌ها", HealthLevel.Critical, $"{pending.Count} مایگریشن اعمال نشده است.", "برنامه را دوباره راه‌اندازی کنید تا مایگریشن‌ها اعمال شوند."));

                var since = DateTimeOffset.UtcNow.AddHours(-1);
                var errors = await context.ErrorLogs.CountAsync(e => e.CreatedAt >= since, cancellationToken);
                checks.Add(errors switch
                {
                    0 => new HealthCheck("خطاهای یک ساعت اخیر", HealthLevel.Healthy, "خطایی ثبت نشده است."),
                    < 20 => new HealthCheck("خطاهای یک ساعت اخیر", HealthLevel.Warning, $"{errors} خطا ثبت شده است.", "صفحه «خطاها» را بررسی کنید."),
                    _ => new HealthCheck("خطاهای یک ساعت اخیر", HealthLevel.Critical, $"{errors} خطا ثبت شده است.", "صفحه «خطاها» را فوراً بررسی کنید."),
                });

                var settings = await scope.ServiceProvider.GetRequiredService<IStoreSettingService>().GetAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(settings.ZarinPalMerchantId))
                    checks.Add(new HealthCheck("درگاه پرداخت", HealthLevel.Warning, "کد مرچنت زرین‌پال تنظیم نشده است.", "از «تنظیمات › پرداخت» وارد کنید."));
                else if (settings.ZarinPalSandbox && environment.IsProduction())
                    checks.Add(new HealthCheck("درگاه پرداخت", HealthLevel.Warning, "درگاه در حالت آزمایشی (سندباکس) است.", "پیش از فروش واقعی حالت آزمایشی را خاموش کنید."));
                else
                    checks.Add(new HealthCheck("درگاه پرداخت", HealthLevel.Healthy, settings.ZarinPalSandbox ? "زرین‌پال (سندباکس)" : "زرین‌پال (واقعی)"));

                var stuck = await context.PaymentTransactions.CountAsync(p =>
                    (p.Status == Data.Entities.Payments.PaymentTransactionStatus.Pending || p.Status == Data.Entities.Payments.PaymentTransactionStatus.Verifying)
                    && p.CreatedAt < DateTimeOffset.UtcNow.AddHours(-2) && p.CreatedAt > DateTimeOffset.UtcNow.AddDays(-2), cancellationToken);
                if (stuck > 0)
                    checks.Add(new HealthCheck("پرداخت‌های معلق", HealthLevel.Warning, $"{stuck} تراکنش بیش از ۲ ساعت در انتظار مانده است.", "در «مالی › تراکنش‌ها» بررسی کنید."));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                checks.Add(new HealthCheck("پایگاه داده", HealthLevel.Critical, "خطا در اجرای بررسی‌ها: " + exception.Message));
            }
        }

        checks.Add(CheckDisk());
        checks.Add(CheckWritable("پوشه داده‌های خصوصی", Path.Combine(environment.ContentRootPath, "App_Data")));
        checks.Add(CheckWritable("پوشه آپلودها", Path.Combine(environment.WebRootPath, "uploads")));

        foreach (var job in JobMonitor.All)
        {
            if (JobMonitor.IsPaused(job.Name))
            {
                checks.Add(new HealthCheck("کار پس‌زمینه: " + job.Name, HealthLevel.Warning, "به دستور کارمند متوقف شده است (کارهای پس‌زمینه)."));
                continue;
            }
            if (job.IsFailing)
                checks.Add(new HealthCheck("کار پس‌زمینه: " + job.Name, HealthLevel.Warning, "آخرین اجرا ناموفق بود: " + job.LastError));
            else if (job.LastSucceededAt is { } last && DateTimeOffset.UtcNow - last > job.ExpectedInterval * 3)
                checks.Add(new HealthCheck("کار پس‌زمینه: " + job.Name, HealthLevel.Warning, "مدتی است اجرا نشده است."));
        }

        if (JobMonitor.All.Count > 0 && JobMonitor.All.All(j => !j.IsFailing))
            checks.Add(new HealthCheck("کارهای پس‌زمینه", HealthLevel.Healthy, $"{JobMonitor.All.Count} کار فعال است."));

        var memory = Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d;
        checks.Add(memory > 1500
            ? new HealthCheck("حافظه", HealthLevel.Warning, $"مصرف حافظه بالاست ({memory:0} مگابایت).")
            : new HealthCheck("حافظه", HealthLevel.Healthy, $"{memory:0} مگابایت"));

        var minutes = tracker.Minutes(5);
        var requests = minutes.Sum(m => m.Requests);
        var serverErrors = minutes.Sum(m => m.ServerErrors);
        checks.Add(requests > 20 && serverErrors * 100 / requests >= 5
            ? new HealthCheck("پاسخ‌های سرور", HealthLevel.Critical, $"{serverErrors} خطای ۵۰۰ از {requests} درخواست در ۵ دقیقه اخیر.")
            : new HealthCheck("پاسخ‌های سرور", HealthLevel.Healthy, $"{requests} درخواست، {serverErrors} خطای ۵۰۰ در ۵ دقیقه اخیر."));

        if (errorQueue.Dropped > 0)
            checks.Add(new HealthCheck("ثبت خطاها", HealthLevel.Warning, $"{errorQueue.Dropped} خطا به دلیل حجم زیاد ثبت نشد."));

        if (smsSender is Infrastructure.Sms.ConfigurableSmsSender configurable)
            checks.Add(await SmsCheckAsync(configurable, cancellationToken));

        if (!environment.IsProduction())
            checks.Add(new HealthCheck("محیط اجرا", HealthLevel.Warning, $"برنامه در محیط «{environment.EnvironmentName}» اجرا می‌شود."));

        return checks;
    }

    /// <summary>
    /// وضعیت سرویس پیامک (بدون ارسال پیامک): تنظیم بودن کلید/خط/قالب و نتیجهٔ آخرین ارسال از زمان روشن شدن برنامه.
    /// راه‌حل‌ها به صفحهٔ «فنی ← سرویس پیامک» اشاره می‌کنند. حداکثر «هشدار» است، نه «بحرانی»: /healthz با وضعیت بحرانی ۵۰۳ می‌دهد
    /// و نبودن پیامک نباید سرویس مانیتورینگ/لود بالانسر را به خیال از کار افتادن کل سایت بیندازد (ورود با رمز و خرید کار می‌کند).
    /// </summary>
    private async Task<HealthCheck> SmsCheckAsync(Infrastructure.Sms.ConfigurableSmsSender sender, CancellationToken cancellationToken)
    {
        const string Name = "پیامک";
        const string Fix = "در «فنی ← سرویس پیامک» تنظیمات SMS.ir را کامل و پیامک آزمایشی بفرستید.";
        try
        {
            var (provider, keySource, config) = await sender.DescribeAsync(cancellationToken);
            var production = environment.IsProduction();
            if (provider != global::Fratak_Shop.Data.Entities.Settings.SmsProvider.SmsIr)
                return new HealthCheck(Name, production ? HealthLevel.Warning : HealthLevel.Healthy, "سرویس پیامک روی «فقط لاگ» است؛ کد ورود و اعلان‌ها پیامک نمی‌شوند.", Fix);
            if (keySource is null)
                return new HealthCheck(Name, HealthLevel.Warning, "کلید API سرویس SMS.ir ثبت نشده است.", Fix);
            if (config.OtpTemplateId is null && config.LineNumber is null)
                return new HealthCheck(Name, HealthLevel.Warning, "نه قالب کد تأیید و نه خط ارسال SMS.ir ثبت شده است.", Fix);

            var stats = sender.Diagnostics;
            if (stats.LastAttemptFailed)
                return new HealthCheck(Name, HealthLevel.Warning, $"آخرین ارسال ناموفق بود: {stats.LastFailure}", Fix);
            return new HealthCheck(Name, HealthLevel.Healthy, $"SMS.ir (کلید از {keySource})؛ {stats.Sent} پیامک موفق و {stats.Failed} ناموفق از زمان روشن شدن.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HealthCheck(Name, HealthLevel.Warning, "وضعیت سرویس پیامک خوانده نشد: " + exception.GetType().Name, Fix);
        }
    }

    public RuntimeInfo Runtime()
    {
        using var process = Process.GetCurrentProcess();
        ThreadPool.GetAvailableThreads(out var worker, out _);
        return new RuntimeInfo(
            environment.EnvironmentName,
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Environment.MachineName,
            Environment.ProcessorCount,
            ProcessStarted,
            DateTimeOffset.UtcNow - ProcessStarted,
            Math.Round(process.WorkingSet64 / 1024d / 1024d, 1),
            Math.Round(GC.GetTotalMemory(false) / 1024d / 1024d, 1),
            GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2),
            Math.Round(process.TotalProcessorTime.TotalSeconds, 1),
            process.Threads.Count,
            worker,
            ThreadPool.PendingWorkItemCount,
            GCSettings.IsServerGC ? "Server" : "Workstation",
            typeof(HealthService).Assembly.GetName().Version?.ToString() ?? "-");
    }

    private HealthCheck CheckDisk()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(environment.ContentRootPath));
            var drive = new DriveInfo(root!);
            var freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            var percent = drive.TotalSize == 0 ? 100 : drive.AvailableFreeSpace * 100d / drive.TotalSize;
            return freeGb < 1 || percent < 5
                ? new HealthCheck("فضای دیسک", HealthLevel.Critical, $"فقط {freeGb:0.0} گیگابایت ({percent:0}٪) آزاد است.", "فایل‌های قدیمی و لاگ‌ها را پاک کنید.")
                : percent < 15
                    ? new HealthCheck("فضای دیسک", HealthLevel.Warning, $"{freeGb:0.0} گیگابایت ({percent:0}٪) آزاد است.")
                    : new HealthCheck("فضای دیسک", HealthLevel.Healthy, $"{freeGb:0.0} گیگابایت ({percent:0}٪) آزاد است.");
        }
        catch (Exception exception)
        {
            return new HealthCheck("فضای دیسک", HealthLevel.Warning, "اندازه‌گیری ممکن نشد: " + exception.Message);
        }
    }

    private static HealthCheck CheckWritable(string name, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return new HealthCheck(name, HealthLevel.Healthy, "قابل نوشتن است.");
        }
        catch (Exception exception)
        {
            return new HealthCheck(name, HealthLevel.Critical, "امکان نوشتن وجود ندارد: " + exception.Message, "دسترسی پوشه را برای کاربر برنامه باز کنید.");
        }
    }
}
