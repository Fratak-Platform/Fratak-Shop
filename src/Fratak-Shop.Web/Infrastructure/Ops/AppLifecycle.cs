using System.Diagnostics;
using System.Text.Json;

namespace Fratak_Shop.Web.Infrastructure.Ops;

/// <summary>یک دورهٔ اجرای برنامه (از روشن شدن تا خاموش شدن).</summary>
/// <param name="StartedAt">زمان روشن شدن (UTC).</param>
/// <param name="StoppedAt">زمان خاموش شدن عادی؛ null = هنوز روشن است یا ناگهانی متوقف شده.</param>
/// <param name="ProcessId">شمارهٔ پروسه (برای تشخیص «هنوز زنده است؟» هنگام بازیافت هم‌پوشان IIS).</param>
/// <param name="Version">نسخهٔ برنامه.</param>
/// <param name="Crash">اگر برنامه با استثنای مدیریت‌نشده از کار افتاد، نوع و پیام کوتاهش؛ وگرنه null.</param>
/// <param name="LastSeenAt">آخرین «نبض» این اجرا (هر دقیقه). برای اجرای ناگهانی‌تمام‌شده یعنی «تقریباً کی بسته شد».</param>
/// <param name="MemoryMb">حافظهٔ خصوصی پروسه (Private Bytes؛ همان چیزی که سقف حافظهٔ استخر IIS می‌سنجد) در آخرین نبض.</param>
/// <param name="PeakMemoryMb">بیشترین حافظهٔ خصوصی دیده‌شده در این اجرا. اگر توقف ناگهانی با حافظهٔ نزدیک سقف استخر بوده، علت کمبود حافظه است.</param>
public sealed record LifecycleRun(DateTimeOffset StartedAt, DateTimeOffset? StoppedAt, int ProcessId, string Version, string? Crash,
    DateTimeOffset? LastSeenAt = null, int? MemoryMb = null, int? PeakMemoryMb = null);

/// <summary>
/// تاریخچهٔ روشن/خاموش شدن برنامه (مرحلهٔ هفدهم) در فایل <c>App_Data/logs/lifecycle.json</c> (۱۰۰ اجرای آخر).
/// <para>چرا: خطای ۵۰۳ و «خروج ناگهانی همه» معمولاً یعنی برنامه پشت سر هم بازیافت یا متوقف می‌شود (بیکاری استخر IIS،
/// خطای مدیریت‌نشده، کمبود حافظه). بدون این تاریخچه، فقط «مدت روشن بودن» فعلی دیده می‌شد. حالا صفحهٔ «سلامت سامانه»
/// نشان می‌دهد در ۲۴ ساعت اخیر چند بار روشن شده، کدام اجراها ناگهانی تمام شده‌اند و آخرین خطای مرگبار چه بوده است.</para>
/// <para>تشخیص «توقف ناگهانی»: اجرایی که زمان خاموشی ندارد، پروسه‌اش دیگر زنده نیست و اجرای فعلی هم نیست. (IIS هنگام بازیافت
/// چند ثانیه دو پروسه را هم‌زمان نگه می‌دارد؛ پس صرف نبودن زمان خاموشی کافی نیست.)</para>
/// <para>همهٔ متدها بی‌خطرند: نوشتن ناموفق فایل فقط نادیده گرفته می‌شود. محل ثبت: Program.cs.</para>
/// </summary>
public static class AppLifecycle
{
    private const int MaxRuns = 100;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static string? _file;
    private static LifecycleRun? _current;
    private static Timer? _heartbeat;

    /// <summary>اجرای فعلی (null پیش از Start).</summary>
    public static LifecycleRun? Current => _current;

    /// <summary>
    /// ثبت روشن شدن. مثال: <c>AppLifecycle.Start(app.Environment.ContentRootPath, "1.0.0")</c>.
    /// </summary>
    public static void Start(string contentRoot, string version)
    {
        using var process = Process.GetCurrentProcess();
        var run = new LifecycleRun(process.StartTime.ToUniversalTime(), null, Environment.ProcessId, version, null);
        lock (Gate)
        {
            _file = Path.Combine(contentRoot, "App_Data", "logs", "lifecycle.json");
            _current = run;
            Update(runs =>
            {
                runs.RemoveAll(r => r.ProcessId == run.ProcessId && r.StartedAt == run.StartedAt);
                runs.Add(run);
            });
        }

        // نبض هر دقیقه: زمان و حافظه ثبت می‌شود تا اگر پروسه بی‌خبر بسته شد (کمبود حافظه، خطای کتابخانهٔ بومی، کشته شدن
        // به‌خاطر سقف CPU)، معلوم باشد آخرین بار کی و با چه حافظه‌ای زنده بود.
        _heartbeat ??= new Timer(_ => Heartbeat(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// ثبت نبض (زمان + حافظهٔ خصوصی فعلی و بیشینه). هرگز خطا نمی‌دهد؛ خطای callback تایمر پروسه را می‌بندد، پس همه‌چیز گرفته می‌شود.
    /// </summary>
    public static void Heartbeat()
    {
        try
        {
            int memory;
            using (var process = Process.GetCurrentProcess())
                memory = (int)Math.Min(int.MaxValue, process.PrivateMemorySize64 / (1024 * 1024));
            lock (Gate)
            {
                if (_current is not { StoppedAt: null } current)
                    return;
                var updated = current with
                {
                    LastSeenAt = DateTimeOffset.UtcNow,
                    MemoryMb = memory,
                    PeakMemoryMb = Math.Max(memory, current.PeakMemoryMb ?? 0),
                };
                _current = updated;
                Update(runs => Replace(runs, updated));
            }
        }
        catch
        {
            // پایش هرگز نباید برنامه را ببندد.
        }
    }

    /// <summary>ثبت خاموش شدن عادی (ApplicationStopping).</summary>
    public static void Stopping()
    {
        lock (Gate)
        {
            if (_current is not { } current)
                return;
            var stopped = current with { StoppedAt = DateTimeOffset.UtcNow };
            _current = stopped;
            Update(runs => Replace(runs, stopped));
        }
    }

    /// <summary>ثبت خطای مرگبار (AppDomain.UnhandledException) پیش از بسته شدن پروسه.</summary>
    public static void Crash(Exception? exception)
    {
        lock (Gate)
        {
            if (_current is not { } current)
                return;
            var text = exception is null ? "Unknown" : exception.GetType().Name + ": " + exception.Message;
            var crashed = current with { Crash = text.Length <= 500 ? text : text[..500] };
            _current = crashed;
            Update(runs => Replace(runs, crashed));
            WriteCrashDetails(exception);
        }
    }

    /// <summary>
    /// متن کامل خطای مرگبار (با Stack trace) مستقیم در فایل روزانهٔ App_Data/logs/fratak-….log نوشته می‌شود؛ صف لاگ معمولی
    /// پیش از بسته شدن پروسه فرصت نوشتن ندارد. در صفحهٔ «گزارش‌های سرور» دیده می‌شود.
    /// </summary>
    private static void WriteCrashDetails(Exception? exception)
    {
        try
        {
            if (_file is null || exception is null)
                return;
            var now = DateTimeOffset.UtcNow;
            var text = exception.ToString();
            if (text.Length > 8000)
                text = text[..8000];
            var indented = text.Replace("\n", "\n    ");
            var stamp = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(_file)!, LogStore.FileNameFor(now)),
                $"{stamp}Z CRITICAL Fratak.Crash | برنامه با خطای مدیریت‌نشده بسته شد{Environment.NewLine}    {indented}{Environment.NewLine}",
                System.Text.Encoding.UTF8);
        }
        catch
        {
            // در حال بسته شدن هستیم؛ کاری از دست برنمی‌آید.
        }
    }

    /// <summary>اجراهای ثبت‌شده، تازه‌ترین اول.</summary>
    public static IReadOnlyList<LifecycleRun> Runs()
    {
        lock (Gate)
        {
            return Read().OrderByDescending(r => r.StartedAt).ToList();
        }
    }

    /// <summary>
    /// آیا این اجرا ناگهانی تمام شده است؟ (زمان خاموشی ندارد، اجرای فعلی نیست و پروسه‌اش زنده نیست؛ یا خطای مرگبار دارد.)
    /// </summary>
    public static bool IsUnclean(LifecycleRun run)
    {
        if (run.Crash is not null)
            return true;
        if (run.StoppedAt is not null)
            return false;
        if (_current is { } current && current.ProcessId == run.ProcessId && current.StartedAt == run.StartedAt)
            return false;
        return !IsAlive(run.ProcessId, run.StartedAt);
    }

    /// <summary>تعداد روشن شدن‌ها در بازهٔ اخیر (شامل اجرای فعلی).</summary>
    public static int StartsWithin(TimeSpan window)
    {
        var since = DateTimeOffset.UtcNow - window;
        return Runs().Count(r => r.StartedAt >= since);
    }

    /// <summary>اجراهای ناگهانی‌تمام‌شده در بازهٔ اخیر.</summary>
    public static IReadOnlyList<LifecycleRun> UncleanWithin(TimeSpan window)
    {
        var since = DateTimeOffset.UtcNow - window;
        return Runs().Where(r => r.StartedAt >= since && IsUnclean(r)).ToList();
    }

    /// <summary>فقط برای آزمون‌ها: استفاده از فایل دیگر.</summary>
    public static void UseFileForTests(string file)
    {
        lock (Gate)
        {
            _file = file;
            _current = null;
        }
    }

    private static void Replace(List<LifecycleRun> runs, LifecycleRun run)
    {
        var index = runs.FindIndex(r => r.ProcessId == run.ProcessId && r.StartedAt == run.StartedAt);
        if (index >= 0)
            runs[index] = run;
        else
            runs.Add(run);
    }

    private static bool IsAlive(int processId, DateTimeOffset startedAt)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited && Math.Abs((process.StartTime.ToUniversalTime() - startedAt.UtcDateTime).TotalSeconds) < 5;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    private static List<LifecycleRun> Read()
    {
        try
        {
            if (_file is null || !File.Exists(_file))
                return [];
            return JsonSerializer.Deserialize<List<LifecycleRun>>(File.ReadAllText(_file), Json) ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static void Update(Action<List<LifecycleRun>> change)
    {
        if (_file is null)
            return;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                // قفل انحصاری: پروسهٔ هم‌پوشان IIS هم‌زمان نمی‌نویسد.
                using var stream = new FileStream(_file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                List<LifecycleRun> runs;
                try
                {
                    runs = stream.Length == 0 ? [] : JsonSerializer.Deserialize<List<LifecycleRun>>(stream, Json) ?? [];
                }
                catch (JsonException)
                {
                    runs = [];
                }
                change(runs);
                if (runs.Count > MaxRuns)
                    runs = runs.OrderByDescending(r => r.StartedAt).Take(MaxRuns).OrderBy(r => r.StartedAt).ToList();
                stream.SetLength(0);
                stream.Position = 0;
                JsonSerializer.Serialize(stream, runs, Json);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
