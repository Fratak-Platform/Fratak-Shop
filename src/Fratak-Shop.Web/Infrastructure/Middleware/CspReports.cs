using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure.Middleware;

/// <summary>
/// دور ۲۸ (#۱۱۹) – یک گزارش «نقض سیاست امنیت محتوا» (CSP) که مرورگر فرستاده است.
/// </summary>
/// <param name="Directive">بند نقض‌شده (مثلاً «script-src»).</param>
/// <param name="Blocked">منبع مسدودشده (فقط دامنه/نوع، بدون پارامترها؛ مثلاً «https://evil.example» یا «inline»).</param>
/// <param name="Page">مسیر صفحه (بدون querystring).</param>
/// <param name="Count">چند بار همین مورد دیده شد.</param>
/// <param name="LastSeen">آخرین بار.</param>
public sealed record CspViolation(string Directive, string Blocked, string Page, int Count, DateTimeOffset LastSeen);

/// <summary>
/// دور ۲۸ (#۱۱۹ – «نقطهٔ گزارش نقض CSP»): مرورگر هر بار چیزی را به‌خاطر CSP مسدود کند (اسکریپت تزریق‌شده، افزونهٔ مخرب،
/// منبع ناشناخته) گزارش را به <c>/api/csp-report</c> می‌فرستد. گزارش‌ها در حافظه (بدون پایگاه داده، سبک برای هاست ضعیف)
/// گروه‌بندی می‌شوند: «بند + منبع + صفحه» ← شمارش. حداکثر <see cref="MaxEntries"/> مورد؛ قدیمی‌ترین کنار می‌رود.
/// <para>
/// امنیت: بدنه حداکثر ۸ کیلوبایت، هر IP حداکثر ۳۰ گزارش در دقیقه، متن‌ها کوتاه و پاک‌سازی می‌شوند (فقط مبدأ آدرس نگه داشته
/// می‌شود تا توکن/اطلاعات شخصی داخل آدرس ذخیره نشود). نمایش در «فنی ← گزارش امنیتی».
/// </para>
/// </summary>
public static class CspReports
{
    public const int MaxEntries = 200;
    public const int MaxBody = 8 * 1024;
    public const string Path = "/api/csp-report";

    private static readonly ConcurrentDictionary<string, CspViolation> Entries = new(StringComparer.Ordinal);

    /// <summary>گزارش‌های ثبت‌شده، پرتکرارترین اول.</summary>
    public static IReadOnlyList<CspViolation> Snapshot(int take = 50) =>
        Entries.Values.OrderByDescending(e => e.Count).ThenByDescending(e => e.LastSeen).Take(Math.Clamp(take, 1, MaxEntries)).ToList();

    /// <summary>پاک کردن همه (آزمون‌ها و دکمهٔ پنل).</summary>
    public static void Clear() => Entries.Clear();

    /// <summary>
    /// خواندن بدنهٔ گزارش (دو قالب: قدیمی <c>{"csp-report":{...}}</c> و جدید Reporting API <c>[{"type":"csp-violation","body":{...}}]</c>).
    /// خروجی: تعداد موارد ثبت‌شده. JSON خراب ← ۰ (بدون خطا).
    /// </summary>
    public static int Record(string json, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBody)
            return 0;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            var added = 0;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("csp-report", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
            {
                added += Add(Text(legacy, "effective-directive") ?? Text(legacy, "violated-directive"), Text(legacy, "blocked-uri"), Text(legacy, "document-uri"), now);
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray().Take(10))
                {
                    if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object)
                        continue;
                    added += Add(Text(body, "effectiveDirective"), Text(body, "blockedURL"), Text(body, "documentURL"), now);
                }
            }
            return added;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>
    /// فقط «مبدأ» آدرس (scheme://host) یا کلیدواژه‌های مرورگر (inline، eval، data، blob) نگه داشته می‌شود.
    /// مثال: «https://cdn.evil.com/x.js?token=1» ← «https://cdn.evil.com»؛ «inline» ← «inline».
    /// </summary>
    public static string CleanSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(نامعلوم)";
        value = value.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return Cut(uri.GetLeftPart(UriPartial.Authority), 120);
        var word = new string(value.TakeWhile(c => char.IsAsciiLetterOrDigit(c) || c is '-' or ':').ToArray()).TrimEnd(':');
        return word.Length == 0 ? "(نامعلوم)" : Cut(word, 40);
    }

    /// <summary>فقط مسیر صفحه (بدون دامنه و querystring). مثال: «https://shop.fratak.ir/cart?x=1» ← «/cart».</summary>
    public static string CleanPage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "/";
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ? Cut(uri.AbsolutePath, 150) : "/";
    }

    private static int Add(string? directive, string? blocked, string? page, DateTimeOffset now)
    {
        var cleanDirective = new string((directive ?? "").Trim().TakeWhile(c => char.IsAsciiLetter(c) || c == '-').ToArray());
        if (cleanDirective.Length == 0)
            cleanDirective = "unknown";
        cleanDirective = Cut(cleanDirective, 40);
        var source = CleanSource(blocked);
        var path = CleanPage(page);
        var key = cleanDirective + "|" + source + "|" + path;
        if (!Entries.ContainsKey(key) && Entries.Count >= MaxEntries)
        {
            // جا نیست ← قدیمی‌ترین کنار می‌رود.
            var oldest = Entries.OrderBy(e => e.Value.LastSeen).FirstOrDefault();
            if (oldest.Key is not null)
                Entries.TryRemove(oldest.Key, out _);
        }
        Entries.AddOrUpdate(key, _ => new CspViolation(cleanDirective, source, path, 1, now), (_, old) => old with { Count = old.Count + 1, LastSeen = now });
        return 1;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>ثبت نقطهٔ گزارش. پاسخ همیشه ۲۰۴ (مرورگر منتظر محتوا نیست).</summary>
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Path, async (HttpContext http, IMemoryCache cache) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (http.Request.ContentLength is > MaxBody)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var rateKey = "csp:rate:" + ip;
            var count = cache.GetOrCreate(rateKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                entry.Size = 1;
                return new int[1];
            })!;
            if (Interlocked.Increment(ref count[0]) > 30)
                return Results.NoContent();
            var buffer = new char[MaxBody + 1];
            using var reader = new StreamReader(http.Request.Body);
            var read = 0;
            while (read <= MaxBody)
            {
                var n = await reader.ReadAsync(buffer.AsMemory(read, buffer.Length - read), http.RequestAborted);
                if (n == 0)
                    break;
                read += n;
            }
            if (read > MaxBody)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            Record(new string(buffer, 0, read), DateTimeOffset.UtcNow);
            return Results.NoContent();
        }).DisableAntiforgery();
    }
}
