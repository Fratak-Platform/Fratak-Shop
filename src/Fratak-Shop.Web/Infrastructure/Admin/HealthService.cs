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
    ISmsSender smsSender,
    Microsoft.Extensions.Options.IOptions<ShopOptions> shopOptions,
    Core.Utilities.IExternalSourceGate sourceGate,
    Auth.DefaultAdminPasswords defaultAdminPasswords,
    Auth.JwtSigningKeyProvider jwtKeys,
    IConfiguration configuration)
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

                // مرحلهٔ سیزدهم (چک‌لیست راه‌اندازی): دادهٔ پیش‌فرض عمداً تلفن/ایمیل/آدرس ساختگی ندارد؛ یادآوری برای پر کردن.
                if (string.IsNullOrWhiteSpace(settings.SupportPhone) && string.IsNullOrWhiteSpace(settings.SupportEmail))
                    checks.Add(new HealthCheck("اطلاعات تماس", HealthLevel.Warning, "تلفن و ایمیل پشتیبانی فروشگاه وارد نشده است (در فوتر، تماس با ما و فاکتور نمایش داده می‌شود).",
                        "از «تنظیمات › عمومی» تلفن، ایمیل، آدرس و ساعت کاری را وارد کنید."));

                // حساب‌های دادهٔ نمونه رمز 123456 دارند؛ روی سایت واقعی باید حذف یا مسدود شوند.
                if (environment.IsProduction())
                {
                    var demoPhones = Seeders.Round6DemoSeeder.DemoStaff.Select(s => s.Phone).Append(Seeders.DemoDataSeeder.DemoPhone).Append("09120000001").ToList();
                    var demoAccounts = await context.Users.CountAsync(u => demoPhones.Contains(u.PhoneNumber) && !u.IsBlocked, cancellationToken);
                    if (demoAccounts > 0)
                        checks.Add(new HealthCheck("حساب‌های نمونه", HealthLevel.Warning, $"{demoAccounts} حساب دادهٔ نمونه (با رمز 123456) روی سایت واقعی فعال است.",
                            "این حساب‌ها را از «مشتریان» و «تیم» حذف یا مسدود کنید و Shop:SeedDemoData را خاموش نگه دارید."));
                }

                // مرحلهٔ چهاردهم: مدیر پیش‌فرضی که هنوز رمز اولیهٔ تنظیمات (Shop:DefaultAdmins) را دارد = خطر جدی روی سایت واقعی.
                if (defaultAdminPasswords.Phones.Count > 0)
                {
                    // مرحلهٔ پانزدهم: مدیر اصلی با کلید ثابت پیدا می‌شود (حتی اگر شماره‌اش را عوض کرده)؛ حساب‌های بی‌کلید با شماره.
                    var phones = defaultAdminPasswords.Phones.ToList();
                    var keys = defaultAdminPasswords.Keys.ToList();
                    var admins = await context.Users
                        .Where(u => (keys.Contains(u.SystemAdminKey!) || (u.SystemAdminKey == null && phones.Contains(u.PhoneNumber))) && !u.IsBlocked && u.PasswordHash != null)
                        .Select(u => new { u.PhoneNumber, u.PasswordHash, u.SystemAdminKey }).ToListAsync(cancellationToken);
                    var weak = admins.Count(x => x.SystemAdminKey is not null
                        ? defaultAdminPasswords.StillUsesDefaultForKey(x.SystemAdminKey, x.PasswordHash)
                        : defaultAdminPasswords.StillUsesDefault(x.PhoneNumber, x.PasswordHash));
                    if (weak > 0)
                        checks.Add(new HealthCheck("رمز مدیران پیش‌فرض", HealthLevel.Warning,
                            $"{weak} مدیر پیش‌فرض هنوز با رمز اولیهٔ تنظیمات وارد می‌شود.",
                            "هر کدام از «پروفایل › رمز عبور» رمز تازه بگذارند (رمز اولیه فقط برای ساخت حساب است و برنامه هرگز رمز تازه را برنمی‌گرداند)."));
                }

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
        // مرحلهٔ هفدهم: پوشهٔ کلیدهای DataProtection (توکن فرم‌ها، کلید نشست‌ها): اگر نوشتنی نباشد بعد از هر ری‌استارت فرم‌ها خطا می‌دهند.
        checks.Add(CheckWritable("پوشه کلیدهای امنیتی", configuration["Security:DataProtectionKeysPath"] is { Length: > 0 } keysPath
            ? keysPath : Path.Combine(environment.ContentRootPath, "App_Data", "keys")));

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

        // مرحلهٔ دوازدهم: سرویس ایمیل.
        if (await EmailCheckAsync(cancellationToken) is { } emailCheck)
            checks.Add(emailCheck);

        // مرحلهٔ سیزدهم: منابع بیرونی قیمت رقبا و موتور سئو (ترب، دیجی‌کالا، باسلام، گوگل، بینگ، داک‌داک‌گو).
        var sources = sourceGate.Snapshot();
        var cooling = sources.Where(source => source.CoolingUntil is not null).ToList();
        if (cooling.Count > 0)
            checks.Add(new HealthCheck("منابع قیمت و سئو", HealthLevel.Warning,
                "موقتاً محدود: " + string.Join("، ", cooling.Select(c => $"{c.Source} (حدود {Math.Max(1, (int)Math.Ceiling((c.CoolingUntil!.Value - DateTimeOffset.UtcNow).TotalMinutes))} دقیقهٔ دیگر)")),
                "کاری لازم نیست: تا پایان استراحت به این منابع درخواستی فرستاده نمی‌شود و منابع دیگر و قیمت‌های ذخیره‌شده استفاده می‌شوند."));
        else if (sources.Count > 0)
            checks.Add(new HealthCheck("منابع قیمت و سئو", HealthLevel.Healthy, $"{sources.Count} منبع در دسترس؛ {sources.Sum(source => source.Requests)} درخواست از زمان روشن شدن."));

        if (!environment.IsProduction())
            checks.Add(new HealthCheck("محیط اجرا", HealthLevel.Warning, $"برنامه در محیط «{environment.EnvironmentName}» اجرا می‌شود."));

        // ── مرحلهٔ هفدهم: آمادگی سرور واقعی (علت‌های ۴۰۳/۵۰۳/خروج ناگهانی) – ProductionDiagnostics ──
        checks.Add(ProductionDiagnostics.SigningKey(jwtKeys.Source, jwtKeys.Problem));
        if (ProductionDiagnostics.CanonicalHost(shopOptions.Value.CanonicalHost, Middleware.CanonicalHostMiddleware.SeenHosts,
                shopOptions.Value.CanonicalAliases ?? [], environment.IsProduction()) is { } canonical)
            checks.Add(canonical);
        if (ProductionDiagnostics.Proxy(Ops.RequestMonitor.ForwardedUntrusted, Ops.RequestMonitor.UntrustedProxies()) is { } proxy)
            checks.Add(proxy);
        checks.Add(ProductionDiagnostics.DefaultAdmins(DefaultAdminStartupReport.Notes, DefaultAdminStartupReport.Problems));
        checks.Add(ProductionDiagnostics.Lifecycle(Ops.AppLifecycle.StartsWithin(TimeSpan.FromHours(24)), Ops.AppLifecycle.UncleanWithin(TimeSpan.FromHours(24))));
        var (windowsEvents, windowsProblem) = await Ops.WindowsEvents.RecentAsync(environment.ContentRootPath, cancellationToken);
        if (ProductionDiagnostics.WindowsCrashes(OperatingSystem.IsWindows(), windowsEvents, windowsProblem, DateTimeOffset.UtcNow) is { } windowsCheck)
            checks.Add(windowsCheck);
        checks.Add(ProductionDiagnostics.Rejections(Ops.RequestMonitor.LastMinutes(60), Ops.RequestMonitor.LostSessionsLastHour()));
        if (Ops.StartupGate.Attempts > 1)
            checks.Add(new HealthCheck("اتصال پایگاه داده هنگام شروع", HealthLevel.Warning,
                $"آماده‌سازی پایگاه داده در این اجرا {Ops.StartupGate.Attempts} تلاش لازم داشت" + (Ops.StartupGate.LastError is { } startupError ? " (آخرین خطا: " + startupError + ")." : "."),
                "پایداری شبکه تا SQL Server (185.191.78.226) را بررسی کنید؛ برنامه خودش دوباره تلاش می‌کند و از کار نمی‌افتد."));
        checks.AddRange(ProductionDiagnostics.Natives(NativeLibraries.Skia, NativeLibraries.Onnx));
        if (!Environment.Is64BitProcess && environment.IsProduction())
            checks.Add(new HealthCheck("پروسهٔ ۳۲ بیتی", HealthLevel.Warning, "برنامه در پروسهٔ ۳۲ بیتی اجرا می‌شود (حداکثر حافظه کم است).",
                "در Plesk/IIS گزینهٔ «Enable 32-Bit Applications» استخر برنامه را خاموش کنید."));

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

    /// <summary>
    /// وضعیت سرویس ایمیل (مرحلهٔ دوازدهم، بدون اتصال به سرور): خاموش/فقط لاگ = سالم (انتخاب مدیر است)؛ SMTP ناقص یا شکست
    /// آخرین ارسال = هشدار. مثل پیامک هرگز «بحرانی» نیست. اگر سرویس ایمیل ثبت نشده باشد (مثلاً در تست‌ها) null.
    /// </summary>
    private async Task<HealthCheck?> EmailCheckAsync(CancellationToken cancellationToken)
    {
        const string Name = "ایمیل";
        const string Fix = "در «فنی ← سرویس ایمیل» تنظیمات SMTP را کامل کنید، «بررسی اتصال» و ایمیل آزمایشی بزنید.";
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            if (scope.ServiceProvider.GetService<Email.EmailDelivery>() is not { } delivery)
                return null;
            var config = await delivery.GetConfigAsync(cancellationToken);
            if (config.Mode != global::Fratak_Shop.Data.Entities.Settings.EmailMode.Smtp)
                return new HealthCheck(Name, HealthLevel.Healthy, (config.NotReadyReason ?? "ارسال ایمیل خاموش است") + "؛ اعلان‌ها فقط پیامکی/درون‌سایتی‌اند.");
            if (config.NotReadyReason is { } reason)
                return new HealthCheck(Name, HealthLevel.Warning, reason + ".", Fix);
            var stats = delivery.Diagnostics;
            if (stats.LastAttemptFailed)
                return new HealthCheck(Name, HealthLevel.Warning, $"آخرین ارسال ناموفق بود: {stats.LastFailure}", Fix);
            return new HealthCheck(Name, HealthLevel.Healthy, $"SMTP ({config.Host})؛ {stats.Sent} ایمیل موفق و {stats.Failed} ناموفق از زمان روشن شدن.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new HealthCheck(Name, HealthLevel.Warning, "وضعیت سرویس ایمیل خوانده نشد: " + exception.GetType().Name, Fix);
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
