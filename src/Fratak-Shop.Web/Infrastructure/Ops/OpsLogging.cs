using System.Text;

namespace Fratak_Shop.Web.Infrastructure.Ops;

// ═══════════════════════════════════════════════════════════════════════════
//  نمایشگر گزارش‌های سرور (مرحلهٔ هفتم – فاز ۹)
//  ۱) آخرین ۲۰۰۰ پیام گزارش (Information به بالا) در حافظه نگه داشته می‌شود تا در «فنی ← گزارش‌های سرور» دیده شود
//     (بدون نیاز به دسترسی SSH به سرور).
//  ۲) هشدارها و خطاها (Warning به بالا) در فایل روزانهٔ App_Data/logs/fratak-YYYY-MM-DD.log هم نوشته می‌شوند تا بعد از
//     ری‌استارت از دست نروند. فایل‌های قدیمی‌تر از «نگهداری فایل‌های گزارش» (تنظیمات) را OpsWorker پاک می‌کند.
//  خطاها (Error به بالا) جداگانه در جدول ErrorLogs هم ثبت می‌شوند (DbErrorLogger) و صفحهٔ «خطاها» جزئیات کامل دارد.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک پیام گزارش. Id افزایشی است تا صفحه بتواند «پیام‌های تازه از آخرین بار» را بگیرد.</summary>
public sealed record LogEntry(long Id, DateTimeOffset At, LogLevel Level, string Category, string Message, string? Exception, string? Path, string? TraceId);

/// <summary>
/// صف حلقوی با ظرفیت ثابت: وقتی پر شد، قدیمی‌ترین مورد جای خود را به تازه می‌دهد. امن برای چند رشته.
/// مثال: new RingBuffer&lt;int&gt;(3) و افزودن ۱،۲،۳،۴ → Snapshot() = [۴، ۳، ۲] (تازه‌ترین اول).
/// </summary>
public sealed class RingBuffer<T>
{
    private readonly T[] _items;
    private readonly object _lock = new();
    private int _next;
    private int _count;

    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count
    {
        get { lock (_lock) return _count; }
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            _items[_next] = item;
            _next = (_next + 1) % _items.Length;
            if (_count < _items.Length)
                _count++;
        }
    }

    /// <summary>کپی محتوا، تازه‌ترین اول.</summary>
    public List<T> Snapshot()
    {
        lock (_lock)
        {
            var list = new List<T>(_count);
            for (var i = 1; i <= _count; i++)
                list.Add(_items[(_next - i + _items.Length) % _items.Length]);
            return list;
        }
    }
}

/// <summary>انبار پیام‌های گزارش (حافظه + فایل روزانه). یک نمونه برای کل برنامه (singleton).</summary>
public sealed class LogStore
{
    public const int Capacity = 2000;
    private const int MaxMessage = 4000;
    private const int MaxException = 8000;

    private readonly RingBuffer<LogEntry> _buffer = new(Capacity);
    private readonly object _fileLock = new();
    private long _nextId;
    private long _warnings;
    private long _errors;

    public LogStore(IWebHostEnvironment environment)
    {
        Folder = Path.Combine(environment.ContentRootPath, "App_Data", "logs");
    }

    /// <summary>پوشهٔ فایل‌های روزانه.</summary>
    public string Folder { get; }

    public long WarningsSinceStart => Interlocked.Read(ref _warnings);

    public long ErrorsSinceStart => Interlocked.Read(ref _errors);

    public List<LogEntry> Snapshot() => _buffer.Snapshot();

    public void Add(LogLevel level, string category, string message, Exception? exception, string? path, string? traceId)
    {
        var entry = new LogEntry(Interlocked.Increment(ref _nextId), DateTimeOffset.UtcNow, level, category,
            Truncate(message, MaxMessage), exception is null ? null : Truncate(exception.ToString(), MaxException), path, traceId);
        _buffer.Add(entry);
        if (level >= LogLevel.Error)
            Interlocked.Increment(ref _errors);
        else if (level == LogLevel.Warning)
            Interlocked.Increment(ref _warnings);
        if (level >= LogLevel.Warning)
            WriteToFile(entry);
    }

    /// <summary>نام فایل روز. مثال: fratak-2026-10-26.log (تاریخ میلادی برای مرتب شدن درست فایل‌ها).</summary>
    public static string FileNameFor(DateTimeOffset utc) => $"fratak-{utc.UtcDateTime:yyyy-MM-dd}.log";

    /// <summary>
    /// نام فایل معتبر برای دانلود (جلوگیری از خواندن فایل‌های دیگر سرور).
    /// مثال: «fratak-2026-10-26.log» → true؛ «../appsettings.json» → false.
    /// </summary>
    public static bool IsValidFileName(string? name) =>
        name is { Length: 21 } && name.StartsWith("fratak-", StringComparison.Ordinal) && name.EndsWith(".log", StringComparison.Ordinal)
        && DateOnly.TryParseExact(name[7..17], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);

    /// <summary>فهرست فایل‌های روزانه (تازه‌ترین اول).</summary>
    public List<FileInfo> Files()
    {
        try
        {
            return Directory.Exists(Folder)
                ? new DirectoryInfo(Folder).GetFiles("fratak-*.log").Where(f => IsValidFileName(f.Name)).OrderByDescending(f => f.Name).ToList()
                : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <summary>حذف فایل‌های قدیمی‌تر از days روز. خروجی: تعداد فایل حذف‌شده.</summary>
    public int Prune(int days, DateTimeOffset now)
    {
        var cutoff = FileNameFor(now.AddDays(-Math.Max(1, days)));
        var removed = 0;
        foreach (var file in Files().Where(f => string.CompareOrdinal(f.Name, cutoff) < 0))
        {
            try
            {
                file.Delete();
                removed++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return removed;
    }

    private void WriteToFile(LogEntry entry)
    {
        try
        {
            var line = new StringBuilder()
                .Append(entry.At.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)).Append("Z ")
                .Append(entry.Level.ToString().ToUpperInvariant()).Append(' ')
                .Append(entry.Category).Append(" | ")
                .Append(entry.Path is null ? "" : entry.Path + " | ")
                .Append(entry.Message.Replace('\n', ' ').Replace('\r', ' '))
                .AppendLine();
            if (entry.Exception is not null)
                line.AppendLine("    " + entry.Exception.Replace("\n", "\n    "));
            lock (_fileLock)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, FileNameFor(entry.At)), line.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // نوشتن گزارش هرگز نباید درخواست یا کار پس‌زمینه را خراب کند (مثلاً دیسک پر).
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}

/// <summary>
/// ارائه‌دهندهٔ گزارش که پیام‌ها را به LogStore می‌دهد. فیلترهای appsettings (Logging:LogLevel) روی آن هم اعمال می‌شود؛
/// خودش هم پیام‌های پرحجم Microsoft/System زیر Warning را نگه نمی‌دارد.
/// </summary>
public sealed class MemoryLogProvider(LogStore store, IHttpContextAccessor accessor) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new MemoryLogger(categoryName, store, accessor);

    public void Dispose()
    {
    }

    /// <summary>
    /// آیا این پیام نگه داشته شود؟ مثال: (Microsoft.AspNetCore.Hosting، Information) → false؛ (Fratak_Shop...، Information) → true.
    /// </summary>
    public static bool Accepts(string category, LogLevel level)
    {
        if (level < LogLevel.Information || level == LogLevel.None)
            return false;
        var framework = category.StartsWith("Microsoft.", StringComparison.Ordinal) || category.StartsWith("System.", StringComparison.Ordinal);
        return !framework || level >= LogLevel.Warning || category == "Microsoft.Hosting.Lifetime";
    }

    private sealed class MemoryLogger(string category, LogStore store, IHttpContextAccessor accessor) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => Accepts(category, logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            try
            {
                var http = accessor.HttpContext;
                var message = formatter(state, exception);
                store.Add(logLevel, category, string.IsNullOrWhiteSpace(message) ? exception?.Message ?? "-" : message, exception,
                    http?.Request.Path.Value, http?.TraceIdentifier);
            }
            catch (Exception)
            {
                // ثبت گزارش هرگز نباید خطا بدهد.
            }
        }
    }
}

/// <summary>برچسب فارسی و رنگ سطح‌ها برای صفحهٔ گزارش‌ها.</summary>
public static class LogLevels
{
    public static string Title(LogLevel level) => level switch
    {
        LogLevel.Critical => "بحرانی",
        LogLevel.Error => "خطا",
        LogLevel.Warning => "هشدار",
        LogLevel.Information => "اطلاع",
        _ => level.ToString(),
    };

    public static string Css(LogLevel level) => level switch
    {
        LogLevel.Critical or LogLevel.Error => "a-badge-red",
        LogLevel.Warning => "a-badge-yellow",
        _ => "a-badge-gray",
    };
}
