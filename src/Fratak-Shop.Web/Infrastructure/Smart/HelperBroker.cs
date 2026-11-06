using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Fratak_Shop.Core.Services.Smart;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>
/// یک کار برای «دستیار محلی» (A31). Kind: «fetch» = دریافت صفحه/JSON از اینترنتِ کامپیوتر مدیر؛
/// «llm» = بازنویسی روان یک متن با Ollama محلی (اختیاری). مثال: (fetch، «https://api.digikala.com/v2/product/123/»).
/// </summary>
public sealed record HelperJob(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("accept")] string? Accept,
    [property: JsonPropertyName("prompt")] string? Prompt);

/// <summary>نتیجهٔ کار دستیار. Status کد HTTP (۰ = خطای اتصال).</summary>
public sealed record HelperResult(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("error")] string? Error);

/// <summary>
/// واسط «دستیار محلی» (A31): برنامهٔ کوچک tools/Fratak-Helper روی کامپیوتر مدیر اجرا می‌شود و با «کلید دستیار» به سایت وصل است.
/// <para>چرا؟ روی هاست اشتراکی، IP سرور گاهی توسط دیجی‌کالا/بینگ محدود می‌شود یا اصلاً اینترنت بین‌الملل ندارد. اگر دستیار روشن باشد،
/// درخواست‌های محتوای هوشمند از اینترنت خود مدیر می‌روند و پردازش سنگین (Ollama) هم روی کامپیوتر او انجام می‌شود – بدون هزینه.</para>
/// <para>روش: دستیار هر بار تا ۲۵ ثانیه منتظر کار می‌ماند (long-poll، بدون پورت باز روی کامپیوتر مدیر). سایت کاری را در صف می‌گذارد و
/// تا ۲۰ ثانیه منتظر نتیجه می‌ماند؛ اگر دستیار ۲ دقیقه دیده نشده یا جواب نداد، همان درخواست مستقیم از سرور انجام می‌شود (هیچ‌وقت گیر نمی‌کند).</para>
/// <para>امنیت: کلید فقط به شکل هش در دیتابیس است؛ مقایسه زمان‌ثابت؛ آدرس‌هایی که دستیار دریافت می‌کند را فقط سرور تعیین می‌کند
/// (نه دستیار) و نتیجه مثل هر پاسخ بیرونی دیگر با پارسرهای امن خوانده می‌شود.</para>
/// <para>ثبت: Singleton در WebServiceCollectionExtensions؛ مسیرها در <see cref="HelperEndpoints"/>.</para>
/// </summary>
public sealed class HelperBroker
{
    /// <summary>بیشترین انتظار دستیار برای یک کار.</summary>
    public static readonly TimeSpan PollWait = TimeSpan.FromSeconds(25);

    /// <summary>بیشترین انتظار سایت برای نتیجهٔ دستیار.</summary>
    public static readonly TimeSpan ResultWait = TimeSpan.FromSeconds(20);

    /// <summary>دستیاری که این مدت درخواست نداده «خاموش» حساب می‌شود.</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(2);

    /// <summary>حداکثر حجم بدنهٔ نتیجه (۳ مگابایت متن).</summary>
    public const int MaxBodyChars = 3_000_000;

    private readonly Channel<HelperJob> _jobs = Channel.CreateBounded<HelperJob>(new BoundedChannelOptions(32)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = false,
        SingleWriter = false,
    });

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<HelperResult>> _pending = new();
    private long _lastPollTicks;

    /// <summary>آخرین بار که دستیار درخواست داد.</summary>
    public DateTimeOffset? LastPoll
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastPollTicks);
            return ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
        }
    }

    /// <summary>دستیار در ۲ دقیقهٔ اخیر دیده شده؟</summary>
    public bool IsOnline(DateTimeOffset now) => LastPoll is { } seen && now - seen < OnlineWindow;

    /// <summary>تعداد کارهای در انتظار نتیجه (برای صفحهٔ تنظیمات).</summary>
    public int Pending => _pending.Count;

    /// <summary>
    /// فرستادن کار به دستیار و انتظار نتیجه. دستیار خاموش، صف پر یا پایان مهلت ← null (فراخوان مسیر مستقیم را می‌رود).
    /// </summary>
    public async Task<HelperResult?> RunAsync(string kind, string? url, string? accept, string? prompt, CancellationToken cancellationToken)
    {
        if (!IsOnline(DateTimeOffset.UtcNow))
            return null;
        var job = new HelperJob(Guid.NewGuid(), kind, url, accept, prompt);
        var completion = new TaskCompletionSource<HelperResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[job.Id] = completion;
        try
        {
            if (!_jobs.Writer.TryWrite(job))
                return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ResultWait);
            return await completion.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _pending.TryRemove(job.Id, out _);
        }
    }

    /// <summary>
    /// دستیار: کار بعدی (تا ۲۵ ثانیه انتظار). کارهایی که فرستنده‌شان دیگر منتظر نیست رد می‌شوند.
    /// </summary>
    public async Task<HelperJob?> NextAsync(CancellationToken cancellationToken)
    {
        MarkSeen();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PollWait);
        try
        {
            while (await _jobs.Reader.WaitToReadAsync(timeout.Token))
            {
                while (_jobs.Reader.TryRead(out var job))
                {
                    if (_pending.ContainsKey(job.Id))
                        return job;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // پایان عادی انتظار.
        }
        finally
        {
            MarkSeen();
        }
        return null;
    }

    /// <summary>دستیار: ثبت نتیجه. شناسهٔ ناشناخته/منقضی ← false.</summary>
    public bool Complete(HelperResult result)
    {
        MarkSeen();
        if (!_pending.TryGetValue(result.Id, out var completion))
            return false;
        var body = result.Body is { Length: > MaxBodyChars } ? null : result.Body;
        var error = body is null && result.Body is not null ? "حجم پاسخ دستیار بیش از حد مجاز بود." : result.Error;
        return completion.TrySetResult(result with { Body = body, Error = error, Status = body is null && result.Body is not null ? 0 : result.Status });
    }

    public void MarkSeen() => Interlocked.Exchange(ref _lastPollTicks, DateTimeOffset.UtcNow.UtcTicks);
}

/// <summary>
/// مسیرهای «دستیار محلی» (A31). همه با سرآیند <c>X-Helper-Key</c>؛ کلید نادرست ← ۴۰۱ بدون توضیح.
/// <list type="bullet">
/// <item><c>GET /api/helper/next</c> ← ۲۰۰ با کار، یا ۲۰۴ اگر ۲۵ ثانیه کاری نبود.</item>
/// <item><c>POST /api/helper/result</c> ← ثبت نتیجه.</item>
/// <item><c>GET /api/helper/ping</c> ← آزمایش اتصال از برنامهٔ دستیار.</item>
/// </list>
/// </summary>
public static class HelperEndpoints
{
    public const string KeyHeader = "X-Helper-Key";
    public const string NameHeader = "X-Helper-Name";

    public static void MapHelperEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/helper").DisableAntiforgery();

        group.MapGet("/ping", async (HttpContext http, ISmartConfigService config, HelperBroker broker) =>
        {
            if (!await AuthorizedAsync(http, config))
                return Results.Unauthorized();
            broker.MarkSeen();
            return Results.Ok(new { ok = true, server = DateTimeOffset.UtcNow });
        });

        group.MapGet("/next", async (HttpContext http, ISmartConfigService config, HelperBroker broker) =>
        {
            if (!await AuthorizedAsync(http, config))
                return Results.Unauthorized();
            var job = await broker.NextAsync(http.RequestAborted);
            return job is null ? Results.NoContent() : Results.Ok(job);
        });

        group.MapPost("/result", async (HttpContext http, [FromBody] HelperResult result, ISmartConfigService config, HelperBroker broker) =>
        {
            if (!await AuthorizedAsync(http, config))
                return Results.Unauthorized();
            return broker.Complete(result) ? Results.Ok(new { ok = true }) : Results.NotFound();
        });
    }

    private static async Task<bool> AuthorizedAsync(HttpContext http, ISmartConfigService config)
    {
        var key = http.Request.Headers[KeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 200)
            return false;
        // نام دستگاه در هدر به‌صورت درصدی (URL-encoded) می‌آید چون هدر HTTP حروف فارسی نمی‌پذیرد؛ مثال: «%D9%84%D9%BE…» ← «لپ…».
        var name = http.Request.Headers[NameHeader].ToString();
        if (name.Length > 600)
            name = name[..600];
        try
        {
            name = Uri.UnescapeDataString(name);
        }
        catch (UriFormatException)
        {
            // نام خراب ← همان متن خام (فقط برای نمایش است).
        }
        return await config.VerifyHelperKeyAsync(key, string.IsNullOrWhiteSpace(name) ? null : name[..Math.Min(name.Length, 60)], http.RequestAborted);
    }
}
