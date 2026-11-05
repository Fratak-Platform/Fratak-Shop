using System.Collections.Concurrent;

namespace Fratak_Shop.Web.Infrastructure.Admin;

public sealed class JobState
{
    public required string Name { get; init; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? LastSucceededAt { get; set; }
    public DateTimeOffset? LastFailedAt { get; set; }
    public string? LastError { get; set; }
    public int? LastCount { get; set; }
    public long Runs { get; set; }
    public long Failures { get; set; }
    public double? LastDurationMs { get; set; }

    /// <summary>Expected maximum time between two runs (for the "stale job" health check).</summary>
    public TimeSpan ExpectedInterval { get; set; } = TimeSpan.FromHours(1);

    public bool IsFailing => LastFailedAt is { } failed && (LastSucceededAt is null || failed > LastSucceededAt);

    // ── کنترل کارها (مرحلهٔ هفتم – فاز ۹؛ صفحهٔ /admin/system/jobs) ──

    /// <summary>
    /// قابل توقف است؟ کارهای حیاتی صف‌محور (ارسال پوش، اعلان کارکنان، ثبت آمار بازدید) false هستند چون توقفشان
    /// صف حافظه را پر می‌کند و داده از دست می‌رود.
    /// </summary>
    public bool IsPausable { get; set; } = true;

    /// <summary>آخرین باری که به خاطر «متوقف بودن» اجرا نشد.</summary>
    public DateTimeOffset? LastSkippedAt { get; set; }

    public long Skips { get; set; }

    /// <summary>
    /// اجرای فوری (اختیاری). ورودی: نام کارمند؛ خروجی: آیا درخواست در صف قرار گرفت. فقط کارهایی که صف دستی دارند
    /// (موتور سئو، ابزارهای سئو، رهگیری پست، بررسی تأمین‌کننده و …) این را با JobMonitor.SetRunNow پر می‌کنند.
    /// </summary>
    public Func<string, bool>? RunNow { get; set; }

    // ── دور نوزدهم: مدیر منابع (Infrastructure/Ops/ResourceGovernor.cs) ──

    /// <summary>وزن کار (حیاتی/سبک/سنگین) – از JobWeights.Of.</summary>
    public Ops.JobWeight Weight => Ops.JobWeights.Of(Name);

    /// <summary>از کی منتظر نوبت است (دورهٔ آرام شروع، فشار منابع یا کار سنگین دیگر)؛ null یعنی منتظر نیست.</summary>
    public DateTimeOffset? WaitingSince { get; set; }

    /// <summary>دلیل انتظار به فارسی. مثال: «دورهٔ آرام بعد از روشن شدن».</summary>
    public string? WaitReason { get; set; }
}

/// <summary>
/// Process-wide registry of the background jobs (last run, duration, failures, last error) shown on the
/// technical page of the admin panel and used by the health checks to spot stuck / failing jobs.
/// </summary>
public static class JobMonitor
{
    private static readonly ConcurrentDictionary<string, JobState> States = new(StringComparer.Ordinal);

    public static IReadOnlyList<JobState> All => States.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ToList();

    public static void Register(string name, TimeSpan expectedInterval) =>
        States.GetOrAdd(name, n => new JobState { Name = n }).ExpectedInterval = expectedInterval;

    /// <summary>ثبت کاری که نباید قابل توقف باشد (صف‌های حیاتی). مثال: WebPushSender.</summary>
    public static void Register(string name, TimeSpan expectedInterval, bool pausable)
    {
        var state = States.GetOrAdd(name, n => new JobState { Name = n });
        state.ExpectedInterval = expectedInterval;
        state.IsPausable = pausable;
    }

    /// <summary>تعریف «اجرای فوری» برای یک کار. مثال: JobMonitor.SetRunNow(JobName, _ => queue.Request()).</summary>
    public static void SetRunNow(string name, Func<string, bool> runNow) =>
        States.GetOrAdd(name, n => new JobState { Name = n }).RunNow = runNow;

    /// <summary>کار ناموفق یا گیرکرده (بیش از ۳ برابر فاصلهٔ مورد انتظار اجرا نشده)؛ کارهای متوقف حساب نمی‌شوند. محل استفاده: هشدار FailingJobs.</summary>
    public static int CountUnhealthy(DateTimeOffset now) => All.Count(job => !IsPaused(job.Name)
        && (job.IsFailing || (job.LastSucceededAt is { } last && now - last > job.ExpectedInterval * 3)));

    public static JobState? Find(string name) => States.TryGetValue(name, out var state) ? state : null;

    // ── توقف/ادامه ──
    // فهرست کارهای متوقف در جدول BackgroundJobSettings ذخیره می‌شود (IBackgroundJobControl) و OpsWorker هر دقیقه
    // (و صفحهٔ کارها بلافاصله پس از تغییر) آن را با SetPaused در این مجموعهٔ حافظه‌ای می‌ریزد.

    private static volatile HashSet<string> _paused = new(StringComparer.Ordinal);

    public static void SetPaused(IEnumerable<string> names) => _paused = new HashSet<string>(names, StringComparer.Ordinal);

    public static bool IsPaused(string name) => _paused.Contains(name) && (Find(name)?.IsPausable ?? true);

    /// <summary>
    /// قبل از اجرای یک کار صدا زده می‌شود: اگر متوقف است، «رد شد» ثبت می‌کند و true برمی‌گرداند.
    /// مثال: if (JobMonitor.ShouldSkip(JobName)) return;
    /// TrackAsync هم خودش همین را بررسی می‌کند؛ این متد برای جاهایی است که خروجی کار شیء است و نباید null شود.
    /// </summary>
    public static bool ShouldSkip(string name)
    {
        if (!IsPaused(name))
            return false;
        var state = States.GetOrAdd(name, n => new JobState { Name = n });
        lock (state)
        {
            state.LastSkippedAt = DateTimeOffset.UtcNow;
            state.Skips++;
        }
        return true;
    }

    /// <summary>Runs one job iteration and records its outcome. Exceptions are recorded and rethrown.</summary>
    public static async Task<T> TrackAsync<T>(string name, Func<Task<T>> run)
    {
        // کار متوقف‌شده اجرا نمی‌شود و مقدار پیش‌فرض (برای شمارش‌ها صفر) برمی‌گردد.
        if (ShouldSkip(name))
            return default!;
        var state = States.GetOrAdd(name, n => new JobState { Name = n });

        // دور نوزدهم: نوبت گرفتن از مدیر منابع. کار حیاتی بی‌درنگ؛ کار سنگین بعد از دورهٔ آرام شروع، یکی‌یکی و وقتی فشار پایین است.
        // وضعیت «در انتظار» در صفحه‌های «کارهای پس‌زمینه» و «منابع سرور» دیده می‌شود.
        using var lease = await Ops.ResourceGovernor.Current.EnterAsync(name, Ops.JobWeights.Of(name), reason =>
        {
            lock (state)
            {
                state.WaitReason = reason;
                state.WaitingSince = reason is null ? null : state.WaitingSince ?? DateTimeOffset.UtcNow;
            }
        });

        var started = DateTimeOffset.UtcNow;
        lock (state)
        {
            state.LastStartedAt = started;
            state.Runs++;
        }

        try
        {
            var result = await run();
            lock (state)
            {
                state.LastSucceededAt = DateTimeOffset.UtcNow;
                state.LastDurationMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
                state.LastCount = result is int count ? count : null;
            }
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            lock (state)
            {
                state.LastFailedAt = DateTimeOffset.UtcNow;
                state.LastDurationMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
                state.Failures++;
                var text = $"{exception.GetType().Name}: {exception.Message}";
                state.LastError = text.Length > 500 ? text[..500] : text;
            }
            throw;
        }
    }

    public static Task TrackAsync(string name, Func<Task> run) => TrackAsync(name, async () =>
    {
        await run();
        return 0;
    });
}
