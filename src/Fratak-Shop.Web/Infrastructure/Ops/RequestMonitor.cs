using System.Collections.Concurrent;
using System.Net;

namespace Fratak_Shop.Web.Infrastructure.Ops;

/// <summary>
/// پایش «درخواست‌های ردشده» (مرحلهٔ هفدهم): هر جا برنامه خودش درخواستی را رد می‌کند (۴۰۳ دیوار آتش، ۴۲۹ محدودیت تعداد،
/// ۵۰۳ تعمیرات/راه‌اندازی، «دسترسی ندارید» پنل، رفتن به صفحهٔ ورود، خطای توکن ضدجعل فرم و …) یک رویداد کوتاه اینجا ثبت می‌شود.
/// <para>چرا: کاربر فقط «۴۰۳» یا «دسترسی ندارید» می‌بیند و معلوم نیست کدام بخش و چرا ردش کرده. حالا صفحهٔ
/// «فنی ← درخواست‌های ردشده» (/admin/system/access) دقیقاً نشان می‌دهد: چه زمانی، چه نوعی، کدام مسیر، کدام IP و دلیل
/// (مثلاً «قانون IP شمارهٔ …» یا «نیاز به دسترسی orders.manage»). پاسخ‌های ۴۰۳/۵۰۳ که اینجا نیستند از خود برنامه نیامده‌اند
/// (مثلاً فایروال هاست/ModSecurity در Plesk یا توقف استخر برنامهٔ IIS) و راهنمای همان صفحه می‌گوید کجا را ببینید.</para>
/// <para>فقط در حافظه (بدون دیتابیس تا خودش باعث کندی نشود): ۵۰۰ رویداد آخر + شمارندهٔ هر نوع از زمان روشن شدن +
/// شمارش دقیقه‌ای ۶۰ دقیقهٔ اخیر. هزینهٔ ثبت چند میکروثانیه است.</para>
/// <para>نشانه‌های پیکربندی هم اینجا شمرده می‌شوند: درخواست‌هایی که سرآیند X-Forwarded-For داشتند ولی از پراکسی مورد اعتماد
/// نیامده بودند (یعنی سایت پشت CDN/پراکسی است و Security:KnownProxies/KnownNetworks تنظیم نشده).</para>
/// </summary>
public static class RequestMonitor
{
    /// <summary>نوع رد شدن.</summary>
    public enum Kind
    {
        /// <summary>۴۰۳ دیوار آتش برنامه (قانون IP یا ربات).</summary>
        Firewall = 0,

        /// <summary>۴۲۹ محدودیت تعداد درخواست هر IP.</summary>
        RateLimited = 1,

        /// <summary>۵۰۳ حالت تعمیرات.</summary>
        Maintenance = 2,

        /// <summary>۵۰۳ «در حال آماده‌سازی» (پایگاه داده هنوز در دسترس نیست).</summary>
        Starting = 3,

        /// <summary>کارمند وارد شده ولی دسترسی لازم آن صفحهٔ پنل را ندارد (صفحهٔ «دسترسی ندارید»).</summary>
        AdminDenied = 4,

        /// <summary>کاربر واردشده که کارمند نیست سراغ پنل رفته (۴۰۴).</summary>
        NotStaff = 5,

        /// <summary>صفحهٔ نیازمند ورود بدون نشست معتبر ← رفتن به صفحهٔ ورود.</summary>
        LoginRedirect = 6,

        /// <summary>۴۰۰ توکن ضدجعل فرم نامعتبر یا منقضی (معمولاً فرمی که خیلی وقت باز مانده یا کوکی‌ها پاک شده).</summary>
        Antiforgery = 7,

        /// <summary>۴۰۳ مجوز (Authorization) در API یا صفحه.</summary>
        Forbidden = 8,

        /// <summary>دور نوزدهم (A19): سرور خیلی شلوغ بود و درخواست در صف جا نشد (۵۰۳ کوتاه با Retry-After).</summary>
        Overloaded = 9,

        /// <summary>دور نوزدهم (A13): به‌جای خطا یا صفحهٔ «شلوغ/آماده‌سازی»، آخرین نسخهٔ سالم صفحه (۲۰۰) نشان داده شد.</summary>
        Snapshot = 10,

        /// <summary>دور نوزدهم (A18): ربات پرمصرف سئو/هوش مصنوعی آهسته شد (۴۲۹).</summary>
        CrawlerThrottled = 11,
    }

    /// <summary>یک رویداد ردشده.</summary>
    /// <param name="HadSession">درخواست کوکی نشست (توکن تازه‌سازی) داشت؟ اگر «رفتن به ورود» با نشست زیاد باشد یعنی نشست‌ها گم می‌شوند.</param>
    public sealed record Event(DateTimeOffset At, Kind Kind, int Status, string Method, string Path, string Ip, string Host, string Device,
        string? Detail, Guid? UserId, bool HadSession);

    private const int Capacity = 500;
    private const int MinuteSlots = 60;
    private static readonly int KindCount = Enum.GetValues<Kind>().Length;

    private static readonly RingBuffer<Event> Events = new(Capacity);
    private static readonly long[] Totals = new long[KindCount];
    private static readonly Bucket[] Minutes = Enumerable.Range(0, MinuteSlots).Select(_ => new Bucket(KindCount)).ToArray();
    private static readonly ConcurrentDictionary<string, long> UntrustedForwarders = new(StringComparer.Ordinal);
    private static long _forwardedUntrusted;
    private static long _canonicalRedirects;

    /// <summary>نام فارسی هر نوع برای پنل.</summary>
    public static string Title(Kind kind) => kind switch
    {
        Kind.Firewall => "دیوار آتش (۴۰۳)",
        Kind.RateLimited => "محدودیت تعداد درخواست (۴۲۹)",
        Kind.Maintenance => "حالت تعمیرات (۵۰۳)",
        Kind.Starting => "در حال آماده‌سازی (۵۰۳)",
        Kind.AdminDenied => "دسترسی پنل ندارد",
        Kind.NotStaff => "غیرکارمند در پنل (۴۰۴)",
        Kind.LoginRedirect => "ارجاع به صفحهٔ ورود",
        Kind.Antiforgery => "توکن فرم نامعتبر (۴۰۰)",
        Kind.Forbidden => "عدم مجوز (۴۰۳)",
        Kind.Overloaded => "شلوغی بیش از ظرفیت (۵۰۳)",
        Kind.Snapshot => "نمایش نسخهٔ ذخیره‌شدهٔ صفحه (۲۰۰)",
        Kind.CrawlerThrottled => "ربات پرمصرف آهسته شد (۴۲۹)",
        _ => kind.ToString(),
    };

    /// <summary>
    /// ثبت یک رد شدن. مثال: <c>RequestMonitor.Record(http, RequestMonitor.Kind.Firewall, "قانون IP 1a2b…")</c>.
    /// هرگز استثنا نمی‌دهد (پایش نباید خودش درخواست را خراب کند).
    /// </summary>
    public static void Record(HttpContext http, Kind kind, string? detail = null, int? status = null)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var index = (int)kind;
            Interlocked.Increment(ref Totals[index]);
            var minute = now.ToUnixTimeSeconds() / 60;
            var bucket = Minutes[minute % MinuteSlots];
            lock (bucket)
            {
                if (bucket.Minute != minute)
                {
                    bucket.Minute = minute;
                    Array.Clear(bucket.Counts);
                }
                bucket.Counts[index]++;
            }

            var path = http.Request.Path.Value ?? "/";
            if (path.Length > 200)
                path = path[..200];
            var ip = Normalize(http.Connection.RemoteIpAddress);
            Events.Add(new Event(now, kind, status ?? StatusFor(kind), http.Request.Method, path, ip, Clip(http.Request.Host.Host, 100),
                Helpers.Display.Device(http.Request.Headers.UserAgent.ToString()), Clip(detail, 300),
                http.User.GetUserId(), http.Request.Cookies.ContainsKey(Auth.AuthCookies.RefreshCookie)));
        }
        catch
        {
            // پایش هرگز نباید پاسخ را خراب کند.
        }
    }

    /// <summary>شمارش ریدایرکت به دامنهٔ اصلی (بدون ثبت رویداد؛ عادی است).</summary>
    public static void CountCanonicalRedirect() => Interlocked.Increment(ref _canonicalRedirects);

    /// <summary>تعداد ریدایرکت به دامنهٔ اصلی از زمان روشن شدن.</summary>
    public static long CanonicalRedirects => Interlocked.Read(ref _canonicalRedirects);

    /// <summary>
    /// بعد از UseForwardedHeaders صدا زده می‌شود: اگر X-Forwarded-For هنوز روی درخواست مانده، یعنی از فرستنده‌ای آمده که
    /// مورد اعتماد نیست (پشت CDN/پراکسی بدون تنظیم KnownProxies/KnownNetworks) – IP همهٔ کاربران یکی دیده می‌شود.
    /// </summary>
    public static void InspectForwarding(HttpContext http)
    {
        if (!http.Request.Headers.ContainsKey("X-Forwarded-For"))
            return;
        Interlocked.Increment(ref _forwardedUntrusted);
        var ip = Normalize(http.Connection.RemoteIpAddress);
        if (UntrustedForwarders.ContainsKey(ip) || UntrustedForwarders.Count < 50)
            UntrustedForwarders.AddOrUpdate(ip, 1, (_, n) => n + 1);
    }

    /// <summary>تعداد درخواست‌های دارای X-Forwarded-For از فرستندهٔ نامطمئن.</summary>
    public static long ForwardedUntrusted => Interlocked.Read(ref _forwardedUntrusted);

    /// <summary>فرستنده‌های نامطمئنی که X-Forwarded-For فرستاده‌اند (احتمالاً پراکسی/CDN) و تعداد هر کدام.</summary>
    public static IReadOnlyList<(string Ip, long Count)> UntrustedProxies()
        => UntrustedForwarders.OrderByDescending(p => p.Value).Take(20).Select(p => (p.Key, p.Value)).ToList();

    /// <summary>رویدادهای اخیر، تازه‌ترین اول؛ با فیلتر نوع اختیاری.</summary>
    public static IReadOnlyList<Event> Recent(Kind? kind = null, int take = 200)
        => Events.Snapshot().Where(e => kind is null || e.Kind == kind).Take(Math.Clamp(take, 1, Capacity)).ToList();

    /// <summary>شمار هر نوع از زمان روشن شدن.</summary>
    public static IReadOnlyDictionary<Kind, long> TotalsByKind()
        => Enum.GetValues<Kind>().ToDictionary(k => k, k => Interlocked.Read(ref Totals[(int)k]));

    /// <summary>شمار هر نوع در <paramref name="minutes"/> دقیقهٔ اخیر (حداکثر ۶۰).</summary>
    public static IReadOnlyDictionary<Kind, int> LastMinutes(int minutes = 60)
    {
        var result = Enum.GetValues<Kind>().ToDictionary(k => k, _ => 0);
        var current = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        for (var m = current - Math.Clamp(minutes, 1, MinuteSlots) + 1; m <= current; m++)
        {
            var bucket = Minutes[m % MinuteSlots];
            lock (bucket)
            {
                if (bucket.Minute != m)
                    continue;
                for (var i = 0; i < KindCount; i++)
                    result[(Kind)i] += bucket.Counts[i];
            }
        }
        return result;
    }

    /// <summary>IPهایی که در رویدادهای اخیر بیشترین رد شدن را داشته‌اند (برای پیدا کردن «همه پشت یک IP» یا مهاجم).</summary>
    public static IReadOnlyList<(string Ip, int Count, Kind Top)> TopIps(int take = 10)
        => Events.Snapshot()
            .Where(e => DateTimeOffset.UtcNow - e.At <= TimeSpan.FromHours(1))
            .GroupBy(e => e.Ip)
            .Select(g => (g.Key, g.Count(), g.GroupBy(e => e.Kind).OrderByDescending(k => k.Count()).First().Key))
            .OrderByDescending(x => x.Item2)
            .Take(take)
            .ToList();

    /// <summary>
    /// «رفتن به صفحهٔ ورود» در یک ساعت اخیر برای مرورگرهایی که کوکی نشست داشتند (نشانهٔ گم شدن نشست؛ مثلاً کلید امضای
    /// ناپایدار یا بازیافت مکرر برنامه).
    /// </summary>
    public static int LostSessionsLastHour()
        => Events.Snapshot().Count(e => e.Kind == Kind.LoginRedirect && e.HadSession && DateTimeOffset.UtcNow - e.At <= TimeSpan.FromHours(1));

    /// <summary>فقط برای آزمون‌ها: پاک کردن همهٔ شمارنده‌ها و رویدادها.</summary>
    public static void ResetForTests()
    {
        for (var i = 0; i < KindCount; i++)
            Interlocked.Exchange(ref Totals[i], 0);
        foreach (var bucket in Minutes)
        {
            lock (bucket)
            {
                bucket.Minute = -1;
                Array.Clear(bucket.Counts);
            }
        }
        Events.Clear();
        UntrustedForwarders.Clear();
        Interlocked.Exchange(ref _forwardedUntrusted, 0);
        Interlocked.Exchange(ref _canonicalRedirects, 0);
    }

    private static int StatusFor(Kind kind) => kind switch
    {
        Kind.Firewall or Kind.Forbidden => 403,
        Kind.RateLimited => 429,
        Kind.Maintenance or Kind.Starting => 503,
        Kind.NotStaff => 404,
        Kind.Antiforgery => 400,
        _ => 302,
    };

    private static string Normalize(IPAddress? ip)
    {
        if (ip is null)
            return "-";
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        return ip.ToString();
    }

    private static string Clip(string? value, int max)
        => string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value[..max];

    private sealed class Bucket(int kinds)
    {
        public long Minute = -1;
        public readonly int[] Counts = new int[kinds];
    }
}

/// <summary>
/// ثبت شکست توکن ضدجعل فرم‌ها (۴۰۰) در <see cref="RequestMonitor"/> (مرحلهٔ هفدهم). این خطا معمولاً یعنی فرم خیلی وقت باز
/// مانده، کوکی‌ها پاک شده یا کلیدهای DataProtection عوض شده‌اند (مثلاً پوشهٔ App_Data/keys قابل نوشتن نیست).
/// IAlwaysRunResultFilter است چون اعتبارسنجی ضدجعل یک فیلتر مجوز است و بقیهٔ فیلترهای نتیجه را رد می‌کند.
/// ثبت: Program.cs ← AddMvcOptions.
/// </summary>
public sealed class AntiforgeryMonitorFilter : Microsoft.AspNetCore.Mvc.Filters.IAlwaysRunResultFilter
{
    public void OnResultExecuting(Microsoft.AspNetCore.Mvc.Filters.ResultExecutingContext context)
    {
        if (context.Result is Microsoft.AspNetCore.Mvc.Core.Infrastructure.IAntiforgeryValidationFailedResult)
            RequestMonitor.Record(context.HttpContext, RequestMonitor.Kind.Antiforgery,
                context.HttpContext.Request.Cookies.ContainsKey("fratak_af") ? "توکن فرم با کوکی نمی‌خواند (فرم قدیمی یا کلید تغییرکرده)" : "کوکی ضدجعل فرستاده نشده");
    }

    public void OnResultExecuted(Microsoft.AspNetCore.Mvc.Filters.ResultExecutedContext context)
    {
    }
}
