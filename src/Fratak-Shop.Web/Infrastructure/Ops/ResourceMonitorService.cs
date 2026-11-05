using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure.Ops;

/// <summary>
/// یک نقطه از نمودار منابع (یک دقیقه): میانگین و بیشینهٔ حافظه، CPU و درخواست‌های هم‌زمان، و بدترین سطح فشار آن دقیقه.
/// مثال: <c>new ResourcePoint(at, 410, 520, 0.12, 0.6, 3, 18, PressureLevel.Elevated)</c>.
/// </summary>
public sealed record ResourcePoint(DateTimeOffset At, double MemoryMb, double PeakMemoryMb, double Cpu, double PeakCpu, double Requests, int PeakRequests, PressureLevel Worst);

/// <summary>
/// رویداد مهم منابع (فشرده‌سازی کش، جمع‌آوری اجباری حافظه، ری‌استارت کنترل‌شده، تغییر سطح فشار) برای صفحهٔ «منابع سرور».
/// </summary>
public sealed record ResourceEvent(DateTimeOffset At, string Text);

/// <summary>
/// تاریخچهٔ منابع در حافظه (۳ ساعت آخر، دقیقه‌ای) + ۵۰ رویداد آخر + بیشینه‌ها از زمان روشن شدن (A1).
/// <para>Singleton؛ نویسنده: <see cref="ResourceMonitorService"/>؛ خواننده: صفحهٔ /admin/system/resources.</para>
/// </summary>
public sealed class ResourceHistory
{
    public const int MaxPoints = 180;
    public const int MaxEvents = 50;

    private readonly object _gate = new();
    private readonly LinkedList<ResourcePoint> _points = new();
    private readonly LinkedList<ResourceEvent> _events = new();
    private readonly List<ResourceSample> _minute = [];
    private long _currentMinute = -1;

    /// <summary>بیشترین حافظهٔ دیده‌شده از زمان روشن شدن (مگابایت).</summary>
    public double PeakMemoryMb { get; private set; }

    /// <summary>بیشترین درخواست هم‌زمان دیده‌شده.</summary>
    public int PeakRequests { get; private set; }

    /// <summary>
    /// افزودن یک نمونه؛ نمونه‌های هر دقیقه در یک نقطه خلاصه می‌شوند. مثال: ۱۲ نمونهٔ ۵ثانیه‌ای ← یک نقطه.
    /// </summary>
    public void Add(ResourceSample sample, PressureLevel level)
    {
        lock (_gate)
        {
            PeakMemoryMb = Math.Max(PeakMemoryMb, sample.MemoryMb);
            PeakRequests = Math.Max(PeakRequests, sample.ActiveRequests);
            var minute = sample.At.ToUnixTimeSeconds() / 60;
            if (_currentMinute != minute && _minute.Count > 0)
                Flush();
            _currentMinute = minute;
            _minute.Add(sample);
            _levels = (PressureLevel)Math.Max((int)_levels, (int)level);
        }
    }

    private PressureLevel _levels;

    private void Flush()
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(_currentMinute * 60);
        _points.AddLast(new ResourcePoint(at,
            _minute.Average(s => s.MemoryMb), _minute.Max(s => s.MemoryMb),
            _minute.Average(s => s.CpuRatio), _minute.Max(s => s.CpuRatio),
            _minute.Average(s => s.ActiveRequests), _minute.Max(s => s.ActiveRequests),
            _levels));
        while (_points.Count > MaxPoints)
            _points.RemoveFirst();
        _minute.Clear();
        _levels = PressureLevel.Normal;
    }

    /// <summary>ثبت رویداد. مثال: <c>history.Note("فشرده‌سازی کش حافظه (۲۵٪)")</c>.</summary>
    public void Note(string text)
    {
        lock (_gate)
        {
            _events.AddFirst(new ResourceEvent(DateTimeOffset.UtcNow, text));
            while (_events.Count > MaxEvents)
                _events.RemoveLast();
        }
    }

    public IReadOnlyList<ResourcePoint> Points()
    {
        lock (_gate)
            return _points.ToList();
    }

    public IReadOnlyList<ResourceEvent> Events()
    {
        lock (_gate)
            return _events.ToList();
    }
}

/// <summary>
/// نمونه‌بردار منابع (A1، A5، A10، A25): هر ۵ ثانیه حافظه/CPU/صف را می‌سنجد، سطح فشار را در <see cref="ResourceGovernor"/> ثبت می‌کند
/// و واکنش نشان می‌دهد:
/// <list type="bullet">
/// <item>فشار «بالا»: فشرده‌سازی ۲۵٪ کش حافظه (حداکثر دقیقه‌ای یک بار).</item>
/// <item>فشار «بحرانی»: فشرده‌سازی ۵۰٪ کش + جمع‌آوری کامل حافظه (حداکثر هر ۲ دقیقه).</item>
/// <item>خط قرمز طولانی حافظه: ری‌استارت کنترل‌شده طبق <see cref="SelfRestartPolicy"/> (پیش از آنکه IIS پروسه را بکشد).</item>
/// </list>
/// <para>ثبت: WebServiceCollectionExtensions (AddHostedService). در آزمون‌های سرتاسری حذف می‌شود (فشار همیشه Normal).</para>
/// </summary>
public sealed class ResourceMonitorService(
    IMemoryCache memoryCache,
    ResourceHistory history,
    IHostApplicationLifetime lifetime,
    IWebHostEnvironment environment,
    ILogger<ResourceMonitorService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private DateTimeOffset _lastCompact = DateTimeOffset.MinValue;
    private DateTimeOffset _lastCollect = DateTimeOffset.MinValue;
    private DateTimeOffset? _redSince;
    private PressureLevel _lastLevel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var meter = new ProcessMeter();
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    Tick(meter);
                }
                catch (Exception exception)
                {
                    // نمونه‌برداری هرگز نباید برنامه را بیندازد.
                    logger.LogDebug(exception, "Resource sampling failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Tick(ProcessMeter meter)
    {
        var governor = ResourceGovernor.Current;
        var sample = meter.Measure(governor);
        var level = governor.Update(sample);
        history.Add(sample, level);
        var now = sample.At;

        if (level != _lastLevel)
        {
            if (level >= PressureLevel.High || _lastLevel >= PressureLevel.High)
                history.Note($"سطح فشار: {Title(_lastLevel)} ← {Title(level)} (حافظه {sample.MemoryMb:0} از {sample.LimitMb:0} مگابایت، درخواست هم‌زمان {sample.ActiveRequests})");
            _lastLevel = level;
        }

        if (level >= PressureLevel.High && now - _lastCompact > TimeSpan.FromMinutes(1) && memoryCache is MemoryCache cache)
        {
            var percent = level == PressureLevel.Critical ? 0.5 : 0.25;
            cache.Compact(percent);
            _lastCompact = now;
            history.Note($"فشرده‌سازی کش حافظه ({percent:P0})");
        }

        if (level == PressureLevel.Critical && sample.MemoryRatio >= 0.92 && now - _lastCollect > TimeSpan.FromMinutes(2))
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            _lastCollect = now;
            history.Note("جمع‌آوری کامل حافظه (GC) به خاطر نزدیک شدن به سقف");
        }

        _redSince = sample.MemoryRatio >= 0.92 ? _redSince ?? now : null;
        var iranHour = Fratak_Shop.Core.Utilities.IranClock.ToLocal(now).Hour;
        var marker = Path.Combine(environment.ContentRootPath, "App_Data", "logs", "self-restart.txt");
        if (SelfRestartPolicy.ShouldRestart(governor.Options, now, governor.StartedAt, _redSince, LastRestart(marker), sample.MemoryRatio, iranHour))
        {
            var reason = $"ری‌استارت کنترل‌شده: حافظه {sample.MemoryMb:0} از {sample.LimitMb:0} مگابایت از {_redSince:HH:mm} UTC";
            logger.LogWarning("Controlled restart: memory {Memory:0} MB of {Limit:0} MB since {Since}", sample.MemoryMb, sample.LimitMb, _redSince);
            history.Note(reason);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
                File.WriteAllText(marker, now.ToString("O"));
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Writing the self-restart marker failed");
            }
            AppLifecycle.SetStopReason(reason);
            _redSince = null;
            lifetime.StopApplication();
        }
    }

    private static DateTimeOffset? LastRestart(string marker)
    {
        try
        {
            return File.Exists(marker) && DateTimeOffset.TryParse(File.ReadAllText(marker).Trim(), out var at) ? at : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>نام فارسی سطح فشار. مثال: High → «بالا».</summary>
    public static string Title(PressureLevel level) => level switch
    {
        PressureLevel.Critical => "بحرانی",
        PressureLevel.High => "بالا",
        PressureLevel.Elevated => "افزایش‌یافته",
        _ => "عادی",
    };
}
