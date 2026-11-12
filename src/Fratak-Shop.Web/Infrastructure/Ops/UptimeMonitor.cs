using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Ops;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.Ops;

/// <summary>
/// دور ۲۴ (پیشنهاد ۷۳): پایشگر داخلی دسترس‌پذیری (رایگان، بدون سرویس بیرونی).
/// <para>
/// هر دقیقه: ۱) «ضربان» (beat|زمان) در <c>App_Data/logs/heartbeat.txt</c> نوشته می‌شود؛
/// ۲) اتصال پایگاه داده با سقف ۵ ثانیه سنجیده می‌شود. اگر قطع باشد، زمان شروع در حافظه می‌ماند و پس از وصل شدن،
///    قطعی با بازهٔ واقعی در <see cref="IUptimeService"/> ثبت و به کارکنان فنی اعلان می‌شود.
/// </para>
/// <para>
/// هنگام روشن شدن: اگر فایل ضربان «beat» باشد (یعنی خاموشی قبلی عادی نبوده: کرش، قطع برق، kill) و فاصله ≥ ۳ دقیقه،
/// از آخرین ضربان تا الان «قطعی سایت» ثبت می‌شود. خاموشی عادی (بیکاری استخر IIS، استقرار) «stopped» می‌نویسد و قطعی حساب نمی‌شود،
/// چون IIS با اولین درخواست دوباره برنامه را روشن می‌کند و بازدیدکننده خطا نمی‌بیند.
/// </para>
/// سبک است: یک نوشتن فایل کوچک و یک «SELECT 1» در دقیقه. نمایش: <c>/admin/system/uptime</c>.
/// </summary>
public sealed class UptimeMonitor(IServiceScopeFactory scopeFactory, IWebHostEnvironment environment, ILogger<UptimeMonitor> logger) : BackgroundService
{
    /// <summary>نام کار در صفحهٔ «کارهای پس‌زمینه».</summary>
    public const string JobName = "پایش دسترس‌پذیری";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private DateTimeOffset? _databaseDownSince;
    private string? _databaseError;
    private bool _startupChecked;
    private (DateTimeOffset Start, DateTimeOffset End)? _pendingGap;

    private string HeartbeatFile => Path.Combine(environment.ContentRootPath, "App_Data", "logs", "heartbeat.txt");

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // قطعی قبلی را پیش از نوشتن ضربان تازه بخوان (وگرنه فایل بازنویسی می‌شود).
        _pendingGap = UptimeRules.GapFromHeartbeat(Read(), DateTimeOffset.UtcNow);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await TickAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // خاموشی عادی: «stopped» ← روشن شدن بعدی قطعی حساب نمی‌کند.
        Write($"stopped|{DateTimeOffset.UtcNow:O}");
        await base.StopAsync(cancellationToken);
    }

    private async Task TickAsync(CancellationToken token)
    {
        Write($"beat|{DateTimeOffset.UtcNow:O}");
        try
        {
            await Admin.JobMonitor.TrackAsync(JobName, () => CheckAsync(token));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Uptime check failed");
        }
    }

    private async Task<int> CheckAsync(CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var now = DateTimeOffset.UtcNow;
        bool reachable;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            reachable = await context.Database.CanConnectAsync(timeout.Token);
            _databaseError = reachable ? null : "اتصال برقرار نشد";
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            reachable = false;
            _databaseError = exception.GetType().Name;
        }

        if (!reachable)
        {
            _databaseDownSince ??= now;
            return 0;
        }

        var uptime = scope.ServiceProvider.GetRequiredService<IUptimeService>();
        var recorded = 0;
        if (!_startupChecked)
        {
            _startupChecked = true;
            if (_pendingGap is { } gap)
            {
                var detail = $"توقف ناگهانی برنامه (آخرین ضربان {Fratak_Shop.Core.Utilities.PersianDate.Format(gap.Start)})";
                if (await uptime.RecordAsync(UptimeIncidentKind.Downtime, gap.Start, gap.End, detail, token) is not null)
                    recorded++;
            }
            _pendingGap = null;
            // نگه‌داری: رخدادهای بیش از یک سال پاک می‌شوند (روزی یک بار کافی است؛ اینجا با هر روشن شدن).
            await uptime.PruneAsync(now, 365, token);
        }

        if (_databaseDownSince is { } since)
        {
            _databaseDownSince = null;
            if (await uptime.RecordAsync(UptimeIncidentKind.DatabaseDown, since, now, _databaseError, token) is not null)
                recorded++;
        }
        return recorded;
    }

    private string? Read()
    {
        try
        {
            return File.Exists(HeartbeatFile) ? File.ReadAllText(HeartbeatFile) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Write(string content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HeartbeatFile)!);
            File.WriteAllText(HeartbeatFile, content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // نوشتن ضربان کمکی است؛ خطای دیسک نباید برنامه را متوقف کند.
        }
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
