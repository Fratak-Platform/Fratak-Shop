using System.Net;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Infrastructure.Middleware;

/// <summary>
/// یک دامنهٔ اصلی برای کل سایت (مرحلهٔ سیزدهم؛ بازنویسی امن در مرحلهٔ هفدهم – دامنهٔ واقعی: shop.fratak.ir).
/// <para>وقتی <see cref="ShopOptions.CanonicalHost"/> تنظیم باشد (مثلاً <c>shop.fratak.ir</c>)، فقط «نام‌های دیگر همین سایت»
/// با ۳۰۱ به همان مسیر روی دامنهٔ اصلی با https می‌روند:</para>
/// <list type="bullet">
/// <item>نسخهٔ www یا بی‌www همان دامنه (مثلاً <c>www.shop.fratak.ir</c> ← <c>shop.fratak.ir</c>)؛</item>
/// <item>دامنه‌هایی که صریحاً در <see cref="ShopOptions.CanonicalAliases"/> آمده‌اند (مثلاً دامنهٔ قدیمی).</item>
/// </list>
/// <para>چرا نه «هر دامنهٔ دیگر» (رفتار قبلی): در مرحلهٔ هفدهم مقدار اشتباه <c>faratek.ir</c> باعث شد کل سایت واقعی
/// (shop.fratak.ir) با ۳۰۱ دائمی به دامنهٔ دیگری برود؛ مرورگرها ۳۰۱ را به خاطر می‌سپارند و خطا حتی بعد از اصلاح تنظیمات
/// می‌ماند (۴۰۳/۵۰۳ از سایت دیگر). حالا دامنهٔ ناشناخته هرگز ریدایرکت نمی‌شود (فقط سرآیند noindex می‌گیرد تا گوگل نسخهٔ
/// تکراری را نبیند) و پاسخ ریدایرکت هم فقط یک ساعت در مرورگر می‌ماند.</para>
/// <para>استثناها: localhost و آدرس IP (آزمایش روی خود سرور و آزمون‌ها)، <c>/health</c> و <c>/healthz</c> (سرویس پایش)
/// و <c>/.well-known/</c> (تأیید گواهی SSL). درخواست غیر GET با ۳۰۸ می‌رود تا متد و بدنه حفظ شود.</para>
/// <para>پایش: دامنه‌هایی که درخواست با آن‌ها آمده شمرده می‌شوند (<see cref="SeenHosts"/>) تا صفحهٔ «سلامت سامانه» اگر
/// دامنهٔ اصلی تنظیمات با دامنهٔ واقعی سایت نخواند هشدار بدهد.</para>
/// <para>محل ثبت: Program.cs، بعد از هدرهای امنیتی و دیوار آتش و پیش از فایل‌های ثابت.</para>
/// </summary>
public sealed class CanonicalHostMiddleware(RequestDelegate next, IOptions<ShopOptions> options)
{
    private const int MaxTrackedHosts = 32;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> Hosts = new(StringComparer.OrdinalIgnoreCase);

    private readonly string? _host = Normalize(options.Value.CanonicalHost);
    private readonly string[] _aliases = (options.Value.CanonicalAliases ?? [])
        .Select(Normalize).Where(h => h is not null).Select(h => h!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>دامنه‌هایی که از زمان روشن شدن برنامه درخواست با آن‌ها آمده (حداکثر ۳۲ دامنه) و تعداد درخواست هر کدام.</summary>
    public static IReadOnlyDictionary<string, long> SeenHosts => new Dictionary<string, long>(Hosts, StringComparer.OrdinalIgnoreCase);

    public Task InvokeAsync(HttpContext context)
    {
        Track(context.Request.Host.Host);
        if (_host is null)
            return next(context);

        var request = context.Request;
        if (!ShouldRedirect(_host, request.Host.Host, request.Path, _aliases))
        {
            // دامنهٔ ناشناخته (مثلاً دامنهٔ موقت هاست): بدون ریدایرکت، ولی موتور جست‌وجو آن را ایندکس نکند.
            if (IsForeign(_host, request.Host.Host, _aliases))
                context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            return next(context);
        }

        var target = $"https://{_host}{request.PathBase}{request.Path}{request.QueryString}";
        context.Response.StatusCode = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            ? StatusCodes.Status301MovedPermanently
            : StatusCodes.Status308PermanentRedirect;
        context.Response.Headers.Location = target;
        Ops.RequestMonitor.CountCanonicalRedirect();
        // ۳۰۱ بدون سرآیند کش برای همیشه در مرورگر می‌ماند؛ یک ساعت یعنی اشتباه تنظیمات زود از بین می‌رود (گوگل باز هم دائمی می‌بیند).
        context.Response.Headers.CacheControl = "public, max-age=3600";
        return Task.CompletedTask;
    }

    /// <summary>سازگاری با فراخوانی‌های قبلی: بدون فهرست دامنه‌های اضافه.</summary>
    public static bool ShouldRedirect(string canonicalHost, string? requestHost, PathString path)
        => ShouldRedirect(canonicalHost, requestHost, path, []);

    /// <summary>
    /// آیا این درخواست باید به دامنهٔ اصلی برود؟ (خالص و تست‌پذیر)
    /// <para>مثال با دامنهٔ اصلی «shop.fratak.ir»: «www.shop.fratak.ir» ← true؛ «SHOP.FRATAK.IR» ← false؛ «fratak.ir» ← false
    /// (سایت دیگری است)؛ «temp.host.ir» ← false (مگر در aliases باشد)؛ «localhost» یا «185.1.2.3» ← false؛
    /// «www.shop.fratak.ir» با مسیر «/health» ← false.</para>
    /// </summary>
    public static bool ShouldRedirect(string canonicalHost, string? requestHost, PathString path, IReadOnlyCollection<string> aliases)
    {
        if (string.IsNullOrEmpty(requestHost) || string.Equals(requestHost, canonicalHost, StringComparison.OrdinalIgnoreCase))
            return false;
        if (IsLocal(requestHost))
            return false;
        if (path.StartsWithSegments("/health") || path.StartsWithSegments("/healthz") || path.StartsWithSegments("/.well-known"))
            return false;
        return IsWwwTwin(canonicalHost, requestHost) || aliases.Contains(requestHost, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// دامنه‌ای که نه دامنهٔ اصلی است، نه نسخهٔ www آن، نه در aliases و نه localhost/IP. مثال: («shop.fratak.ir»، «x.plesk.page») ← true.
    /// </summary>
    public static bool IsForeign(string canonicalHost, string? requestHost, IReadOnlyCollection<string> aliases)
        => !string.IsNullOrEmpty(requestHost)
           && !string.Equals(requestHost, canonicalHost, StringComparison.OrdinalIgnoreCase)
           && !IsLocal(requestHost)
           && !IsWwwTwin(canonicalHost, requestHost)
           && !aliases.Contains(requestHost, StringComparer.OrdinalIgnoreCase);

    /// <summary>نسخهٔ www/بی‌www همان دامنه؟ مثال: («shop.fratak.ir»، «www.shop.fratak.ir») ← true؛ («www.a.ir»، «a.ir») ← true.</summary>
    public static bool IsWwwTwin(string canonicalHost, string requestHost)
        => string.Equals(requestHost, "www." + canonicalHost, StringComparison.OrdinalIgnoreCase)
           || (canonicalHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
               && string.Equals(requestHost, canonicalHost[4..], StringComparison.OrdinalIgnoreCase));

    private static bool IsLocal(string requestHost)
        => string.Equals(requestHost, "localhost", StringComparison.OrdinalIgnoreCase)
           || requestHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
           || IPAddress.TryParse(requestHost.Trim('[', ']'), out _);

    /// <summary>شمارش دامنهٔ درخواست (بی‌هزینه؛ بیش از ۳۲ دامنهٔ متفاوت فقط دامنه‌های قبلی شمرده می‌شوند تا حافظه پر نشود).</summary>
    private static void Track(string? host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 253)
            return;
        if (Hosts.ContainsKey(host) || Hosts.Count < MaxTrackedHosts)
            Hosts.AddOrUpdate(host, 1, (_, count) => count + 1);
    }

    /// <summary>
    /// مقدار تنظیمات به نام دامنهٔ خالص. مثال: «https://www.Faratek.ir/» ← «www.faratek.ir»؛ «faratek.ir» ← «faratek.ir»؛ «» ← null.
    /// <para>دامنهٔ عمومی همیشه نقطه دارد؛ پس «off»، «none» یا «localhost» یعنی خاموش (null). با این، خاموش کردن با متغیر محیطی
    /// <c>Shop__CanonicalHost=off</c> هم ممکن است (مقدار خالی متغیر محیطی در ویندوز یعنی «متغیر وجود ندارد»).</para>
    /// </summary>
    public static string? Normalize(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) && uri.HostNameType == UriHostNameType.Dns && uri.IdnHost.Contains('.')
            ? uri.IdnHost.ToLowerInvariant()
            : null;
    }
}
