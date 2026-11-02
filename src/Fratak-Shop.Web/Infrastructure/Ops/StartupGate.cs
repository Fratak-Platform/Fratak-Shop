using System.Diagnostics;
using System.Text;

namespace Fratak_Shop.Web.Infrastructure.Ops;

/// <summary>
/// «دروازهٔ راه‌اندازی» (مرحلهٔ هفدهم): آماده‌سازی پایگاه داده در شروع برنامه (مهاجرت‌ها، دادهٔ پیش‌فرض، مدیران اصلی، …)
/// اگر به‌خاطر قطعی موقت SQL Server شکست بخورد، دیگر برنامه را از کار نمی‌اندازد.
/// <para>چرا: پایگاه داده روی سرور دیگری است (185.191.78.226)؛ یک قطعی چندثانیه‌ای هنگام بازیافت IIS قبلاً یعنی خطای شروع
/// برنامه ← IIS چند بار پشت سر هم دوباره امتحان می‌کرد ← «Rapid-Fail Protection» استخر برنامه را متوقف می‌کرد ←
/// خطای ۵۰۳ تا وقتی کسی دستی سرور را ری‌استارت کند.</para>
/// <para>رفتار جدید:</para>
/// <list type="number">
/// <item>چند بار با فاصلهٔ فزاینده (۲، ۴، ۸ … ثانیه) تا حداکثر ~۴۰ ثانیه تلاش می‌شود (کمتر از مهلت ۱۲۰ ثانیه‌ای IIS برای شروع).</item>
/// <item>اگر باز هم نشد، برنامه بالا می‌آید ولی همهٔ صفحه‌ها یک صفحهٔ کوتاه «در حال آماده‌سازی» با کد ۵۰۳ و Retry-After
/// نشان می‌دهند و در پس‌زمینه هر ۱۵ تا ۶۰ ثانیه دوباره تلاش می‌شود؛ به‌محض وصل شدن، سایت خودکار باز می‌شود (بدون ری‌استارت).</item>
/// <item>وضعیت در /health و /healthz («starting») و در گزارش‌های سرور دیده می‌شود.</item>
/// </list>
/// <para>محل ثبت: Program.cs (TryRunAsync / RetryInBackground) و <see cref="StartupGateMiddleware"/> اول خط لولهٔ درخواست‌ها.</para>
/// </summary>
public static class StartupGate
{
    private static volatile bool _ready = true;
    private static string? _lastError;
    private static int _attempts;
    private static DateTimeOffset? _waitingSince;

    /// <summary>آیا آماده‌سازی تمام شده و سایت باز است؟ (پیش‌فرض true تا در آزمون‌ها و حالت عادی هیچ اثری نداشته باشد.)</summary>
    public static bool IsReady => _ready;

    /// <summary>آخرین خطای آماده‌سازی (فقط نوع و پیام، برای صفحهٔ سلامت و لاگ) یا null.</summary>
    public static string? LastError => _lastError;

    /// <summary>تعداد تلاش‌های آماده‌سازی از شروع برنامه.</summary>
    public static int Attempts => _attempts;

    /// <summary>از کی سایت منتظر پایگاه داده است (null = منتظر نیست).</summary>
    public static DateTimeOffset? WaitingSince => _waitingSince;

    /// <summary>
    /// اجرای آماده‌سازی با چند تلاش تا سقف زمانی <paramref name="budget"/>. true = موفق.
    /// مثال: SQL Server در تلاش اول در دسترس نیست و ۵ ثانیه بعد هست ← تلاش دوم موفق و true.
    /// </summary>
    public static async Task<bool> TryRunAsync(Func<Task> initialize, ILogger logger, TimeSpan budget, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var delay = TimeSpan.FromSeconds(2);
        while (true)
        {
            if (await AttemptAsync(initialize, logger, cancellationToken))
                return true;
            if (cancellationToken.IsCancellationRequested || watch.Elapsed + delay > budget)
                return false;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 15));
        }
    }

    /// <summary>
    /// سایت را در حالت «در حال آماده‌سازی» می‌گذارد و تا موفقیت در پس‌زمینه تلاش می‌کند (۱۵، ۳۰، ۶۰، ۶۰ … ثانیه).
    /// هیچ استثنایی بیرون نمی‌رود؛ با توقف برنامه تمام می‌شود.
    /// </summary>
    public static void RetryInBackground(Func<Task> initialize, ILogger logger, CancellationToken stopping)
    {
        _ready = false;
        _waitingSince ??= DateTimeOffset.UtcNow;
        logger.LogCritical("Database initialization failed; the site answers 503 (starting) and retries in the background. Last error: {Error}", _lastError);
        _ = Task.Run(async () =>
        {
            var delay = TimeSpan.FromSeconds(15);
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(delay, stopping).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (await AttemptAsync(initialize, logger, stopping))
                {
                    MarkReady();
                    logger.LogWarning("Database initialization succeeded after {Attempts} attempts; the site is open again.", _attempts);
                    return;
                }
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }, CancellationToken.None);
    }

    /// <summary>باز کردن سایت (پس از موفقیت آماده‌سازی؛ در آزمون‌ها هم برای برگرداندن حالت).</summary>
    public static void MarkReady()
    {
        _ready = true;
        _waitingSince = null;
    }

    /// <summary>فقط برای آزمون‌ها: حالت «منتظر» را شبیه‌سازی می‌کند.</summary>
    public static void MarkWaitingForTests(string error)
    {
        _ready = false;
        _lastError = error;
        _waitingSince = DateTimeOffset.UtcNow;
    }

    private static async Task<bool> AttemptAsync(Func<Task> initialize, ILogger logger, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _attempts);
        try
        {
            await initialize().ConfigureAwait(false);
            _lastError = null;
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _lastError = exception.GetType().Name + ": " + Shorten(exception.GetBaseException().Message);
            logger.LogError(exception, "Database initialization attempt {Attempt} failed", _attempts);
            return false;
        }
    }

    private static string Shorten(string message) => message.Length <= 300 ? message : message[..300] + "…";

    /// <summary>صفحهٔ سادهٔ «در حال آماده‌سازی» (مستقل از پایگاه داده و قالب سایت).</summary>
    public static string Page() => """
        <!doctype html>
        <html lang="fa" dir="rtl">
        <head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex">
        <meta http-equiv="refresh" content="30">
        <title>در حال آماده‌سازی…</title>
        <style>
        body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#FDFDFD;color:#0B1927;font-family:Tahoma,sans-serif;padding:24px}
        .box{max-width:460px;background:#fff;border-radius:18px;padding:32px 26px;text-align:center;box-shadow:0 10px 40px rgba(11,25,39,.08);border-top:4px solid #0069FD}
        h1{font-size:20px;margin:0 0 12px}p{line-height:2;color:#475569;margin:0}
        @media (prefers-color-scheme:dark){body{background:#0B1927;color:#E1E8F1}.box{background:#102132;box-shadow:none}p{color:#C9D4E1}}
        </style>
        </head>
        <body>
        <main class="box">
        <h1>فروشگاه در حال آماده‌سازی است</h1>
        <p>چند لحظه دیگر دوباره سر بزنید؛ این صفحه هر ۳۰ ثانیه خودکار تازه می‌شود.</p>
        </main>
        </body>
        </html>
        """;
}

/// <summary>
/// تا وقتی <see cref="StartupGate.IsReady"/> نیست، به همهٔ درخواست‌ها (جز فایل‌های ثابت) ۵۰۳ کوتاه با Retry-After می‌دهد.
/// /health و /healthz پاسخ JSON «starting» می‌گیرند تا سرویس پایش وضعیت را درست ببیند. وقتی آماده است هزینه‌اش یک شرط است.
/// </summary>
public sealed class StartupGateMiddleware(RequestDelegate next)
{
    private static readonly byte[] Html = Encoding.UTF8.GetBytes(StartupGate.Page());

    public async Task InvokeAsync(HttpContext http)
    {
        if (StartupGate.IsReady || http.Request.Path.StartsWithSegments("/assets") || http.Request.Path.Equals("/favicon.ico"))
        {
            await next(http);
            return;
        }

        RequestMonitor.Record(http, RequestMonitor.Kind.Starting, StartupGate.LastError);
        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        http.Response.Headers.RetryAfter = "30";
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Robots-Tag"] = "noindex";
        if (http.Request.Path.StartsWithSegments("/health") || http.Request.Path.StartsWithSegments("/healthz") || http.Request.Path.StartsWithSegments("/api"))
        {
            await http.Response.WriteAsJsonAsync(new { status = "starting", attempts = StartupGate.Attempts }, http.RequestAborted);
            return;
        }

        http.Response.ContentType = "text/html; charset=utf-8";
        http.Response.ContentLength = Html.Length;
        await http.Response.Body.WriteAsync(Html, http.RequestAborted);
    }
}
