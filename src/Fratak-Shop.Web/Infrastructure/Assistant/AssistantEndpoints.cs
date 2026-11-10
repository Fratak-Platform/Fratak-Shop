using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۳ – لایهٔ وب «دستیار فروشگاه»
//   • AssistantLookup    : جست‌وجوی اینترنت در پس‌زمینه برای پرسشی که جوابش در دادهٔ ما نیست (حداکثر ۲ جست‌وجوی هم‌زمان،
//                          مهلت ۱۲ ثانیه، نتیجه ۱۲ ساعت در حافظه). درخواست مشتری هرگز منتظر اینترنت نمی‌ماند.
//   • AssistantEndpoints : POST /api/assistant/message و GET /api/assistant/lookup/{token} (برای assets/js/assistant.js).
//  ثبت: Extensions/WebServiceCollectionExtensions.cs (Singleton) و Program.cs (MapAssistantEndpoints).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// جست‌وجوی اینترنت پس‌زمینه برای دستیار. مثال: Start(«باتری» پاوربانک X) ← توکن؛ مرورگر هر ۱٫۵ ثانیه Poll(توکن) می‌کند تا جواب آماده شود.
/// <para>ایمنی و سبکی: حداکثر ۲ جست‌وجوی هم‌زمان (بقیه ۳ ثانیه صبر و بعد «الان شلوغ است»)؛ حداکثر ۲۰۰ کار در حافظه؛ کار قدیمی‌تر از ۵ دقیقه پاک می‌شود؛
/// همان پرسش برای همان کالا تا ۱۲ ساعت از حافظه جواب می‌گیرد (پرسش تکراری دوباره به اینترنت نمی‌رود). منابع از دروازهٔ مشترک موتور سئو
/// (<see cref="ISeoWebSource"/>) می‌گذرند: بدون کلید، با فاصلهٔ مؤدبانه بین درخواست‌ها و توقف خودکار وقتی منبعی محدودمان کند.</para>
/// </summary>
public sealed class AssistantLookup(ISeoWebSource web, IMemoryCache cache, ILogger<AssistantLookup> logger)
{
    public const int MaxConcurrent = 2;
    public const int MaxJobs = 200;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan JobLifetime = TimeSpan.FromMinutes(5);

    public const string Busy = "الان امکان جست‌وجوی اینترنتی نیست؛ پرسش شما ثبت شد تا مشخصات این کالا کامل شود.";

    private readonly SemaphoreSlim _gate = new(MaxConcurrent, MaxConcurrent);
    private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.Ordinal);

    private sealed class Job
    {
        public DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
        public string? Text { get; set; }
        private volatile bool _done;
        public bool Done => _done;

        public void Finish(string text)
        {
            Text = text;
            _done = true;
        }
    }

    /// <summary>شروع جست‌وجو و برگرداندن توکن ۳۲ نویسه‌ای (هیچ‌وقت منتظر اینترنت نمی‌ماند).</summary>
    public string Start(AssistantLookupNeed need)
    {
        Cleanup();
        var token = Guid.NewGuid().ToString("N");
        var job = new Job();
        if (_jobs.Count >= MaxJobs)
        {
            job.Finish(Busy);
            _jobs[token] = job;
            return token;
        }
        _jobs[token] = job;
        var key = CacheKey(need);
        if (cache.TryGetValue(key, out string? cached) && cached is not null)
        {
            job.Finish(cached);
            return token;
        }
        _ = Task.Run(() => RunAsync(need, key, job));
        return token;
    }

    /// <summary>وضعیت یک توکن: (پیدا شد؟، تمام شد؟، متن). توکن ناشناخته/منقضی ← (false، true، null).</summary>
    public (bool Known, bool Done, string? Text) Poll(string token) =>
        _jobs.TryGetValue(token, out var job) ? (true, job.Done, job.Done ? job.Text : null) : (false, true, null);

    /// <summary>تعداد کارهای در حافظه (برای تست).</summary>
    public int Pending => _jobs.Count;

    /// <summary>کلید حافظهٔ جواب. مثال: «assistant:web:{id}:باتری».</summary>
    public static string CacheKey(AssistantLookupNeed need) => $"assistant:web:{need.ProductId:N}:{need.TopicTitle}";

    private async Task RunAsync(AssistantLookupNeed need, string key, Job job)
    {
        if (!await _gate.WaitAsync(TimeSpan.FromSeconds(3)))
        {
            job.Finish(Busy);
            return;
        }
        try
        {
            using var timeout = new CancellationTokenSource(Timeout);
            var result = await web.SearchAsync(AssistantWeb.Query(need.ProductName, need.TopicTitle), timeout.Token);
            var answer = AssistantWeb.Evaluate(need.ProductName, need.TopicTitle, need.TopicWords.ToList(), result.Ok && result.Value is { } items ? items : Array.Empty<SerpItem>());
            cache.Set(key, answer.Text, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = answer.Sources > 0 ? TimeSpan.FromHours(12) : TimeSpan.FromMinutes(30),
                Size = 1,
            });
            job.Finish(answer.Text);
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "Assistant web lookup failed for {Product}", need.ProductId);
            job.Finish(Busy);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Cleanup()
    {
        var cutoff = DateTimeOffset.UtcNow - JobLifetime;
        foreach (var pair in _jobs)
            if (pair.Value.Started < cutoff)
                _jobs.TryRemove(pair.Key, out _);
    }
}

/// <summary>
/// مسیرهای دستیار فروشگاه.
/// <list type="bullet">
/// <item><c>POST /api/assistant/message</c> – بدنهٔ JSON حداکثر ۴ کیلوبایت:
/// <c>{"text":"…","session":"abc123…","productId":"…","last":["…"],"action":"…"}</c>. هدر ضدجعل <c>RequestVerificationToken</c> لازم است.
/// پاسخ: <c>{text, cards[], actions[], intent, lookup, cartChanged}</c>.</item>
/// <item><c>GET /api/assistant/lookup/{token}</c> – نتیجهٔ جست‌وجوی اینترنت: <c>{done:false}</c> یا <c>{done:true, text}</c>.</item>
/// </list>
/// همهٔ پاسخ‌ها no-store؛ محدودیت نرخ عمومی /api (۶۰ در دقیقه برای هر IP) هم اعمال می‌شود.
/// </summary>
public static partial class AssistantEndpoints
{
    public const int MaxBody = 4096;

    public static void MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/assistant/message", async (HttpContext http, IAntiforgery antiforgery, IAssistantService assistant, ICartSessionService cartSession, AssistantLookup lookup) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (http.Request.ContentLength is > MaxBody)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            if (!await antiforgery.IsRequestValidAsync(http))
                return Results.BadRequest(new { error = "نشست شما منقضی شده است؛ صفحه را دوباره بارگذاری کنید." });

            var input = await ReadAsync(http);
            if (input is null || string.IsNullOrWhiteSpace(input.Text))
                return Results.BadRequest(new { error = "پیام خالی است." });

            var request = new AssistantRequest(
                AssistantNlu.Cut(input.Text),
                Session(input.Session),
                http.User.GetUserId(),
                input.ProductId,
                (input.Last ?? []).Distinct().Take(6).ToList(),
                input.Action,
                async create => create ? (await cartSession.GetOrCreateCartAsync())?.Id : (await cartSession.GetCartAsync())?.Id);
            var reply = await assistant.ReplyAsync(request, http.RequestAborted);
            var token = reply.Lookup is { } need ? lookup.Start(need) : null;
            return Results.Ok(new
            {
                text = reply.Text,
                cards = reply.Cards.Select(c => new { id = c.Id, name = c.Name, url = c.Url, image = c.ImageUrl, price = c.Price, finalPrice = c.FinalPrice, discount = c.DiscountPercent, inStock = c.InStock }),
                actions = reply.Actions.Select(a => new { label = a.Label, url = SafeUrl(a.Url), say = a.Say }),
                intent = reply.Intent.ToString(),
                lookup = token,
                cartChanged = reply.CartChanged,
            });
        }).DisableAntiforgery();

        app.MapGet("/api/assistant/lookup/{token}", (string token, HttpContext http, AssistantLookup lookup) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!TokenRegex().IsMatch(token))
                return Results.BadRequest(new { error = "توکن نامعتبر است." });
            var (known, done, text) = lookup.Poll(token);
            if (!known)
                return Results.Ok(new { done = true, text = (string?)AssistantLookup.Busy });
            return Results.Ok(new { done, text });
        });
    }

    /// <summary>بدنهٔ ورودی پیام.</summary>
    public sealed record AssistantInput(string? Text, string? Session, Guid? ProductId, List<Guid>? Last, Guid? Action);

    /// <summary>خواندن بدنه با سقف ۴ کیلوبایت. بدنهٔ خراب ← null.</summary>
    private static async Task<AssistantInput?> ReadAsync(HttpContext http)
    {
        var buffer = new byte[MaxBody + 1];
        var total = 0;
        int read;
        while (total <= MaxBody && (read = await http.Request.Body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), http.RequestAborted)) > 0)
            total += read;
        if (total == 0 || total > MaxBody)
            return null;
        try
        {
            return JsonSerializer.Deserialize<AssistantInput>(buffer.AsSpan(0, total), JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>شناسهٔ گفت‌وگو: ۸ تا ۳۲ حرف/رقم انگلیسی؛ وگرنه یک شناسهٔ تازه. مثال: «a1b2c3d4e5» ← همان؛ «&lt;x&gt;» ← شناسهٔ تصادفی.</summary>
    public static string Session(string? value) =>
        value is not null && SessionRegex().IsMatch(value) ? value : Guid.NewGuid().ToString("N");

    /// <summary>فقط مسیر داخلی سایت (/x) برای دکمه‌ها؛ «//evil» یا «https://…» ← null.</summary>
    public static string? SafeUrl(string? url) =>
        url is not null && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal) && url.Length <= 300 ? url : null;

    [GeneratedRegex(@"\A[A-Za-z0-9]{8,32}\z")]
    private static partial Regex SessionRegex();

    [GeneratedRegex(@"\A[a-f0-9]{32}\z")]
    private static partial Regex TokenRegex();
}
