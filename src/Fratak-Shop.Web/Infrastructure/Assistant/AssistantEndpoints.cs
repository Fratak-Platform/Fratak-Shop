using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۳ – لایهٔ وب «دستیار فروشگاه»
//   • AssistantLookup    : جست‌وجوی اینترنت در پس‌زمینه برای پرسشی که جوابش در دادهٔ ما نیست (حداکثر ۲ جست‌وجوی هم‌زمان،
//                          مهلت ۱۲ ثانیه، نتیجه ۱۲ ساعت در حافظه). درخواست مشتری هرگز منتظر اینترنت نمی‌ماند.
//   • AssistantEndpoints : POST /api/assistant/message و GET /api/assistant/lookup/{token} (برای assets/js/assistant.js).
//   • دور ۲۴: GET /api/assistant/suggest (پرسش‌های پیشنهادی صفحه)، GET /api/assistant/handoff و POST /api/assistant/handoff/close (کارشناس).
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
                async create => create ? (await cartSession.GetOrCreateCartAsync())?.Id : (await cartSession.GetCartAsync())?.Id)
            {
                LastQuery = CleanQuery(input.LastQuery),
                PagePath = SafeUrl(input.Page),
                Flow = input.Flow is { Length: <= 140 } flow ? flow : null,
                Style = Style(input.Style),
            };
            var reply = await assistant.ReplyAsync(request, http.RequestAborted);
            var token = reply.Lookup is { } need ? lookup.Start(need) : null;
            return Results.Ok(new
            {
                text = reply.Text,
                cards = reply.Cards.Select(c => new { id = c.Id, name = c.Name, url = c.Url, image = c.ImageUrl, price = c.Price, finalPrice = c.FinalPrice, discount = c.DiscountPercent, inStock = c.InStock }),
                actions = Actions(reply.Actions),
                intent = reply.Intent.ToString(),
                lookup = token,
                cartChanged = reply.CartChanged,
                // دور ۲۴: راهنمای زنده، حافظهٔ گفت‌وگو، سلیقهٔ لحن و گفت‌وگو با کارشناس.
                steps = AssistantGuide.Clean(reply.Steps).Select(s => new { kind = s.Kind, url = s.Url, target = s.Target, text = s.Text, value = s.Value }),
                query = reply.Query,
                style = reply.Style is { } st ? new { callMe = st.CallMe, tone = (int)st.Tone, register = st.Register } : null,
                handoff = reply.Handoff,
            });
        }).DisableAntiforgery();

        // دور ۲۴: پرسش‌های پیشنهادی صفحه + نام و معرفی دستیار. مثال: GET /api/assistant/suggest?page=%2Fproduct%2Fx&product={id}
        app.MapGet("/api/assistant/suggest", async (HttpContext http, IAssistantService assistant, string? page, Guid? product, string? callMe, int? tone, int? register) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var suggestion = await assistant.SuggestAsync(SafeUrl(page), product, http.User.GetUserId(),
                Style(new AssistantStyleInput(callMe, tone, register)), http.RequestAborted);
            return Results.Ok(new
            {
                name = suggestion.Name,
                intro = suggestion.Intro,
                chips = Actions(suggestion.Chips),
                greeting = suggestion.Greeting,
                handoff = suggestion.HandoffEnabled,
            });
        });

        // دور ۲۴: پیام‌های کارشناس برای این مرورگر (فقط بعد از زمان after به میلی‌ثانیه). مثال: GET /api/assistant/handoff?session=abc12345&after=0
        app.MapGet("/api/assistant/handoff", async (HttpContext http, IAssistantHandoffService handoffs, string? session, long? after) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (session is null || !SessionRegex().IsMatch(session))
                return Results.BadRequest(new { error = "شناسهٔ گفت‌وگو نامعتبر است." });
            var poll = await handoffs.PollAsync(session, Math.Max(0, after ?? 0), http.RequestAborted);
            return Results.Ok(new
            {
                open = poll.Open,
                status = poll.Status?.ToString(),
                messages = poll.Messages.Select(m => new { id = m.Id, fromStaff = m.FromStaff, author = m.Author, text = m.Text, at = m.At }),
            });
        });

        // دور ۲۴: پایان گفت‌وگو با کارشناس از طرف مشتری (بدنه: {"session":"…"}؛ هدر ضدجعل لازم).
        app.MapPost("/api/assistant/handoff/close", async (HttpContext http, IAntiforgery antiforgery, IAssistantHandoffService handoffs) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (http.Request.ContentLength is > MaxBody)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            if (!await antiforgery.IsRequestValidAsync(http))
                return Results.BadRequest(new { error = "نشست شما منقضی شده است؛ صفحه را دوباره بارگذاری کنید." });
            var input = await ReadAsync(http);
            if (input?.Session is not { } session || !SessionRegex().IsMatch(session))
                return Results.BadRequest(new { error = "شناسهٔ گفت‌وگو نامعتبر است." });
            var closed = await handoffs.CloseByCustomerAsync(session, http.RequestAborted);
            return Results.Ok(new { closed });
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

    /// <summary>بدنهٔ ورودی پیام. دور ۲۴: LastQuery (حافظهٔ گفت‌وگو)، Page (مسیر صفحه)، Flow (جریان دکمه‌ای) و Style (سلیقهٔ مهمان).</summary>
    public sealed record AssistantInput(string? Text, string? Session, Guid? ProductId, List<Guid>? Last, Guid? Action,
        string? LastQuery = null, string? Page = null, string? Flow = null, AssistantStyleInput? Style = null);

    /// <summary>سلیقهٔ گفت‌وگوی مهمان از حافظهٔ مرورگر. مثال: {"callMe":"علی","tone":0,"register":2}.</summary>
    public sealed record AssistantStyleInput(string? CallMe, int? Tone, int? Register);

    /// <summary>
    /// سلیقهٔ ورودی ← سلیقهٔ امن: اسم تمیز (حداکثر ۳۰ نویسه، بدون نماد)، لحن معتبر، طرز نوشتن بین ‎-3 و ‎+3. ورودی خالی ← null.
    /// </summary>
    public static AssistantStyle? Style(AssistantStyleInput? input)
    {
        if (input is null || input.CallMe is null && input.Tone is null && input.Register is null)
            return null;
        var tone = input.Tone is { } t && t is >= 0 and <= 255 && Enum.IsDefined((AssistantTone)(byte)t) ? (AssistantTone)(byte)t : AssistantTone.Auto;
        return new AssistantStyle(AssistantPersona.CleanName(input.CallMe), tone, Math.Clamp(input.Register ?? 0, -AssistantPersona.MaxRegister, AssistantPersona.MaxRegister));
    }

    /// <summary>عبارت حافظهٔ گفت‌وگو: پوشاندن موبایل/کارت و حداکثر ۱۰۰ نویسه. مثال: «هدفون سونی» ← همان؛ «» ← null.</summary>
    public static string? CleanQuery(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var masked = AssistantNlu.Mask(value.Trim());
        return masked.Length > 100 ? masked[..100] : masked;
    }

    /// <summary>دکمه‌ها برای مرورگر؛ فقط مسیر داخلی و جریان معتبر.</summary>
    private static IEnumerable<object> Actions(IEnumerable<AssistantAction> actions) =>
        actions.Select(a => new { label = a.Label, url = SafeUrl(a.Url), say = a.Say, flow = AssistantFinder.Parse(a.Flow) is null ? null : a.Flow });

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
