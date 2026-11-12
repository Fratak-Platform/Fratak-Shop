using System.Collections.Concurrent;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// وضعیت یک منبع بیرونی (ترب، دیجی‌کالا، گوگل، …) برای نمایش در «سلامت سیستم».
/// </summary>
/// <param name="Source">نام منبع، مثلاً «ترب».</param>
/// <param name="CoolingUntil">اگر منبع ما را محدود کرده، تا این زمان سراغش نمی‌رویم؛ null یعنی آزاد است.</param>
/// <param name="Strikes">چند بار پشت‌سرهم محدود شده‌ایم (برای بزرگ‌تر شدن زمان استراحت).</param>
/// <param name="Requests">تعداد درخواست‌ها از زمان روشن شدن برنامه.</param>
/// <param name="Blocks">تعداد دفعاتی که منبع ما را محدود کرد.</param>
/// <param name="LastSuccessAt">آخرین پاسخ سالم.</param>
/// <param name="LastProblem">آخرین مشکل به فارسی.</param>
/// <param name="LastProblemAt">زمان آخرین مشکل.</param>
public sealed record ExternalSourceStatus(
    string Source, DateTimeOffset? CoolingUntil, int Strikes, long Requests, long Blocks,
    DateTimeOffset? LastSuccessAt, string? LastProblem, DateTimeOffset? LastProblemAt);

/// <summary>
/// «دروازهٔ مؤدب» برای سایت‌های بیرونی که API رسمی و رایگان ندارند (مرحلهٔ سیزدهم).
/// <para>چرا لازم است: ترب، دیجی‌کالا و موتورهای جست‌وجو درخواست زیاد و پشت‌سرهم را با کپچا (ترب: کد ۴۹۰)، ۴۲۹ یا ۴۰۳ رد می‌کنند.
/// اگر بعد از رد شدن دوباره و دوباره درخواست بفرستیم، مدت محدودیت طولانی‌تر می‌شود و IP سرور سیاه می‌شود.
/// این کلاس با رفتار درست (نه دور زدن کپچا) کاری می‌کند که منابع کمتر ما را محدود کنند و اگر کردند، سریع‌تر آزاد شویم:</para>
/// <list type="number">
/// <item><b>صف و فاصله:</b> برای هر منبع همیشه فقط یک درخواست در جریان است و بین دو درخواست حداقل فاصله + کمی فاصلهٔ تصادفی رعایت می‌شود
/// (مثلاً ترب ۱٫۵ تا ۲٫۱ ثانیه).</item>
/// <item><b>قطع‌کن مدار (Circuit breaker):</b> با اولین «محدود شدن»، منبع ۱۰ دقیقه استراحت می‌کند؛ اگر دوباره رخ داد ۲۰، ۴۰، … تا حداکثر ۲ ساعت.
/// اگر خود سایت Retry-After فرستاده باشد همان رعایت می‌شود. اولین پاسخ سالم همه‌چیز را صفر می‌کند.</item>
/// <item><b>خطاهای موقت</b> (قطعی شبکه، مهلت): بعد از ۳ خطای پشت‌سرهم، ۲ دقیقه استراحت.</item>
/// </list>
/// <para>کجا استفاده می‌شود: <c>CompetitorPriceService</c> (پیشنهاد قیمت و قیمت‌گذاری بازار درون‌ریزی) و <c>SeoWebSource</c> (موتور سئو).
/// به‌صورت Singleton ثبت می‌شود تا همهٔ درخواست‌های برنامه یک صف مشترک داشته باشند.</para>
/// <example>
/// <code>
/// using var lease = await gate.TryEnterAsync("ترب", TimeSpan.FromSeconds(1.5), ct);
/// if (lease is null) return "ترب فعلاً در حال استراحت است";
/// var response = await http.GetAsync(url, ...);
/// if (response.Status == 490) gate.ReportBlocked("ترب", null, "کپچا"); else gate.ReportSuccess("ترب");
/// </code>
/// </example>
/// </summary>
public interface IExternalSourceGate
{
    /// <summary>
    /// نوبت گرفتن برای یک درخواست. اگر منبع در حال استراحت باشد بلافاصله null برمی‌گرداند (درخواستی فرستاده نمی‌شود)؛
    /// وگرنه تا رسیدن نوبت صبر می‌کند و یک «اجاره» می‌دهد که باید بعد از درخواست Dispose شود.
    /// </summary>
    Task<IDisposable?> TryEnterAsync(string source, TimeSpan minInterval, CancellationToken cancellationToken);

    /// <summary>پاسخ سالم: شمارنده‌های محدودیت و خطا صفر می‌شوند.</summary>
    void ReportSuccess(string source);

    /// <summary>منبع ما را محدود کرد (کپچا، ۴۲۹، ۴۰۳، ۵۰۳): استراحت با زمان دوبرابرشونده یا طبق Retry-After.</summary>
    void ReportBlocked(string source, TimeSpan? retryAfter, string problem);

    /// <summary>خطای موقت (شبکه/مهلت/پاسخ خراب): بعد از ۳ بار پشت‌سرهم استراحت کوتاه.</summary>
    void ReportFailure(string source, string problem);

    /// <summary>اگر منبع در حال استراحت است، زمان پایانش؛ وگرنه null.</summary>
    DateTimeOffset? CoolingUntil(string source);

    /// <summary>وضعیت همهٔ منابعی که از زمان روشن شدن برنامه استفاده شده‌اند.</summary>
    IReadOnlyList<ExternalSourceStatus> Snapshot();
}

/// <summary>
/// پیاده‌سازی درون‌حافظه‌ای <see cref="IExternalSourceGate"/> (بدون وابستگی به پایگاه داده؛ با ری‌استارت برنامه صفر می‌شود که اشکالی ندارد).
/// </summary>
/// <param name="intervalScale">ضریب فاصلهٔ بین درخواست‌ها؛ ۱ در برنامه، ۰ در تست‌ها (تا تست‌ها منتظر نمانند).</param>
public sealed class ExternalSourceGate(double intervalScale = 1) : IExternalSourceGate
{
    /// <summary>اولین استراحت بعد از محدود شدن.</summary>
    public static readonly TimeSpan BaseCooldown = TimeSpan.FromMinutes(10);

    /// <summary>بیشترین استراحت دوبرابرشونده.</summary>
    public static readonly TimeSpan MaxCooldown = TimeSpan.FromHours(2);

    /// <summary>بیشترین Retry-After که می‌پذیریم (عدد عجیب بزرگ، منبع را برای همیشه خاموش نکند).</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(6);

    /// <summary>استراحت بعد از ۳ خطای موقت پشت‌سرهم.</summary>
    public static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(2);

    private const int FailuresBeforeCooldown = 3;

    private readonly ConcurrentDictionary<string, State> states = new(StringComparer.Ordinal);

    /// <summary>وضعیت یک منبع. همهٔ فیلدها زیر lock همین شیء خوانده/نوشته می‌شوند.</summary>
    private sealed class State(string source)
    {
        public readonly string Source = source;
        public readonly SemaphoreSlim Turn = new(1, 1);
        public DateTimeOffset NextAllowedAt;
        public DateTimeOffset? CoolingUntil;
        public int Strikes;
        public int Failures;
        public long Requests;
        public long Blocks;
        public DateTimeOffset? LastSuccessAt;
        public string? LastProblem;
        public DateTimeOffset? LastProblemAt;
    }

    /// <summary>اجارهٔ نوبت: با Dispose، زمان مجاز درخواست بعدی تعیین و نوبت آزاد می‌شود.</summary>
    private sealed class Lease(State state, TimeSpan interval) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 1)
                return;
            lock (state)
                state.NextAllowedAt = DateTimeOffset.UtcNow + interval;
            state.Turn.Release();
        }
    }

    public async Task<IDisposable?> TryEnterAsync(string source, TimeSpan minInterval, CancellationToken cancellationToken)
    {
        var state = Get(source);
        if (CoolingUntil(state) is not null)
            return null;

        await state.Turn.WaitAsync(cancellationToken);
        try
        {
            // ممکن است در مدت انتظار در صف، درخواست قبلی منبع را محدود کرده باشد.
            if (CoolingUntil(state) is not null)
            {
                state.Turn.Release();
                return null;
            }

            DateTimeOffset next;
            lock (state)
                next = state.NextAllowedAt;
            var wait = next - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);

            lock (state)
                state.Requests++;
        }
        catch
        {
            state.Turn.Release();
            throw;
        }

        // فاصلهٔ تصادفی ۰ تا ۴۰٪ تا الگوی درخواست‌ها کاملاً منظم و ماشینی نباشد.
        var interval = TimeSpan.FromMilliseconds(Math.Max(0, minInterval.TotalMilliseconds) * intervalScale * (1 + Random.Shared.NextDouble() * 0.4));
        return new Lease(state, interval);
    }

    public void ReportSuccess(string source)
    {
        var state = Get(source);
        lock (state)
        {
            state.Strikes = 0;
            state.Failures = 0;
            state.CoolingUntil = null;
            state.LastSuccessAt = DateTimeOffset.UtcNow;
        }
    }

    public void ReportBlocked(string source, TimeSpan? retryAfter, string problem)
    {
        var state = Get(source);
        lock (state)
        {
            state.Strikes = Math.Min(state.Strikes + 1, 10);
            state.Blocks++;
            var cooldown = Cooldown(state.Strikes, retryAfter);
            state.CoolingUntil = DateTimeOffset.UtcNow + cooldown;
            state.LastProblem = problem;
            state.LastProblemAt = DateTimeOffset.UtcNow;
        }
    }

    public void ReportFailure(string source, string problem)
    {
        var state = Get(source);
        lock (state)
        {
            state.Failures++;
            state.LastProblem = problem;
            state.LastProblemAt = DateTimeOffset.UtcNow;
            if (state.Failures >= FailuresBeforeCooldown)
            {
                state.Failures = 0;
                state.CoolingUntil = DateTimeOffset.UtcNow + FailureCooldown;
            }
        }
    }

    public DateTimeOffset? CoolingUntil(string source)
        => states.TryGetValue(source, out var state) ? CoolingUntil(state) : null;

    public IReadOnlyList<ExternalSourceStatus> Snapshot()
        => states.Values.Select(s =>
        {
            lock (s)
                return new ExternalSourceStatus(s.Source, CoolingUntilUnlocked(s), s.Strikes, s.Requests, s.Blocks, s.LastSuccessAt, s.LastProblem, s.LastProblemAt);
        }).OrderBy(s => s.Source, StringComparer.Ordinal).ToList();

    /// <summary>
    /// مدت استراحت: ۱۰ دقیقه × ۲^(دفعه−۱)، حداکثر ۲ ساعت؛ اگر Retry-After بزرگ‌تر بود (تا ۶ ساعت) همان.
    /// <para>مثال: دفعهٔ اول ۱۰ دقیقه، دوم ۲۰، سوم ۴۰، چهارم ۸۰، پنجم به بعد ۱۲۰ دقیقه؛ Retry-After=۳ ساعت ← ۳ ساعت.</para>
    /// </summary>
    public static TimeSpan Cooldown(int strikes, TimeSpan? retryAfter)
    {
        var exponent = Math.Clamp(strikes, 1, 10) - 1;
        var backoff = TimeSpan.FromTicks(Math.Min(MaxCooldown.Ticks, BaseCooldown.Ticks * (1L << exponent)));
        if (retryAfter is { } wanted && wanted > backoff)
            return wanted < MaxRetryAfter ? wanted : MaxRetryAfter;
        return backoff;
    }

    private State Get(string source) => states.GetOrAdd(source, name => new State(name));

    private static DateTimeOffset? CoolingUntil(State state)
    {
        lock (state)
            return CoolingUntilUnlocked(state);
    }

    private static DateTimeOffset? CoolingUntilUnlocked(State state)
        => state.CoolingUntil is { } until && until > DateTimeOffset.UtcNow ? until : null;
}
