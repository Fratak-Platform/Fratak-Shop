using System.Text.RegularExpressions;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Reports;

// ═══════════════════════════════════════════════════════════════════════════
//  قواعد «منبع ورودی» بازدیدکننده (مرحلهٔ هفتم – فاز ۷)
//  از روی پارامترهای UTM آدرس و ارجاع‌دهنده (Referrer) می‌فهمیم بازدیدکننده از کجا آمده:
//    ?utm_source=instagram&utm_medium=story&utm_campaign=yalda → (instagram, story, yalda)
//    ارجاع از google.com بدون UTM                               → (google, organic)
//    بدون ارجاع یا از خود سایت                                  → (direct, direct)
//  استفاده: Web/Infrastructure/Analytics (ثبت بازدید)، صفحهٔ /admin/reports/campaigns (عنوان‌ها و سازندهٔ لینک).
//  همه‌چیز داخلی است؛ هیچ سرویس خارجی (مثل Google Analytics) لازم نیست.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// منبع یک بازدید یا سفارش.
/// </summary>
/// <param name="Source">منبع. مثال: «google»، «instagram»، «direct».</param>
/// <param name="Medium">نوع منبع. مثال: «organic»، «social»، «cpc»، «direct».</param>
/// <param name="Campaign">نام کمپین (اختیاری). مثال: «yalda-1405».</param>
public sealed record TrafficTouch(string Source, string Medium, string? Campaign)
{
    public static readonly TrafficTouch Direct = new(TrafficRules.DirectSource, TrafficRules.DirectSource, null);

    public bool IsDirect => Source == TrafficRules.DirectSource;
}

/// <summary>
/// تشخیص منبع ورودی، نوع دستگاه و ربات‌ها + سازندهٔ لینک کمپین.
/// <para>برای افزودن منبع تازه (مثلاً یک سایت مقایسهٔ قیمت جدید) فقط یک خط به <see cref="KnownHosts"/> اضافه کنید.</para>
/// </summary>
public static class TrafficRules
{
    public const string DirectSource = "direct";

    /// <summary>منبع سفارش‌هایی که کارکنان در پنل ثبت کرده‌اند (StaffOrderService).</summary>
    public const string StaffSource = "staff";

    /// <summary>
    /// دامنه‌های شناخته‌شده → (منبع، نوع). تطبیق با «انتهای نام دامنه» است؛ پس «l.instagram.com» هم «instagram» است.
    /// برای گوگل/یاهو که دامنهٔ کشوری دارند (google.co.uk) جداگانه در <see cref="FromHost"/> بررسی می‌شود.
    /// </summary>
    private static readonly (string Host, string Source, string Medium)[] KnownHosts =
    [
        ("bing.com", "bing", "organic"),
        ("duckduckgo.com", "duckduckgo", "organic"),
        ("yandex.ru", "yandex", "organic"),
        ("yandex.com", "yandex", "organic"),
        ("search.yahoo.com", "yahoo", "organic"),
        ("ecosia.org", "ecosia", "organic"),
        ("instagram.com", "instagram", "social"),
        ("t.me", "telegram", "social"),
        ("telegram.org", "telegram", "social"),
        ("telegram.me", "telegram", "social"),
        ("whatsapp.com", "whatsapp", "social"),
        ("wa.me", "whatsapp", "social"),
        ("facebook.com", "facebook", "social"),
        ("fb.com", "facebook", "social"),
        ("twitter.com", "x", "social"),
        ("x.com", "x", "social"),
        ("t.co", "x", "social"),
        ("linkedin.com", "linkedin", "social"),
        ("lnkd.in", "linkedin", "social"),
        ("youtube.com", "youtube", "social"),
        ("youtu.be", "youtube", "social"),
        ("aparat.com", "aparat", "social"),
        ("eitaa.com", "eitaa", "social"),
        ("rubika.ir", "rubika", "social"),
        ("bale.ai", "bale", "social"),
        ("pinterest.com", "pinterest", "social"),
        ("torob.com", "torob", "comparison"),
        ("emalls.ir", "emalls", "comparison"),
        ("mail.google.com", "gmail", "email"),
    ];

    private static readonly Dictionary<string, string> SourceTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        [DirectSource] = "مستقیم", [StaffSource] = "ثبت توسط کارکنان", ["google"] = "گوگل", ["bing"] = "بینگ", ["duckduckgo"] = "داک‌داک‌گو", ["yandex"] = "یاندکس",
        ["yahoo"] = "یاهو", ["ecosia"] = "اکوزیا", ["instagram"] = "اینستاگرام", ["telegram"] = "تلگرام", ["whatsapp"] = "واتساپ",
        ["facebook"] = "فیسبوک", ["x"] = "ایکس (توییتر)", ["linkedin"] = "لینکدین", ["youtube"] = "یوتیوب", ["aparat"] = "آپارات",
        ["eitaa"] = "ایتا", ["rubika"] = "روبیکا", ["bale"] = "بله", ["pinterest"] = "پینترست", ["torob"] = "ترب",
        ["emalls"] = "ایمالز", ["gmail"] = "جیمیل", ["sms"] = "پیامک", ["email"] = "ایمیل", ["push"] = "اعلان مرورگر",
    };

    private static readonly Dictionary<string, string> MediumTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        [DirectSource] = "مستقیم", [StaffSource] = "پنل مدیریت", ["organic"] = "جست‌وجوی طبیعی", ["social"] = "شبکهٔ اجتماعی", ["referral"] = "ارجاع از سایت دیگر",
        ["cpc"] = "تبلیغ کلیکی", ["comparison"] = "مقایسهٔ قیمت", ["email"] = "ایمیل", ["sms"] = "پیامک", ["campaign"] = "کمپین",
        ["story"] = "استوری", ["post"] = "پست", ["banner"] = "بنر", ["affiliate"] = "همکاری در فروش", ["push"] = "اعلان مرورگر",
    };

    /// <summary>فقط حرف (فارسی/انگلیسی)، رقم، خط تیره، زیرخط و نقطه در مقدارهای UTM می‌ماند.</summary>
    private static readonly Regex Unsafe = new(@"[^\p{L}\p{Nd}\-_.]+", RegexOptions.Compiled);

    private static readonly Regex VisitorKeyFormat = new("^[a-f0-9]{32}$", RegexOptions.Compiled);

    /// <summary>
    /// تشخیص منبع یک بازدید.
    /// <para>اولویت: ۱) utm_source  ۲) gclid (تبلیغ گوگل)  ۳) دامنهٔ ارجاع‌دهنده  ۴) مستقیم.</para>
    /// </summary>
    /// <param name="query">رشتهٔ پرس‌وجوی آدرس صفحه. مثال: «?utm_source=insta&amp;utm_campaign=yalda».</param>
    /// <param name="referrer">آدرس صفحهٔ قبلی (document.referrer). مثال: «https://www.google.com/».</param>
    /// <param name="ownHost">دامنهٔ خود فروشگاه تا ارجاع داخلی «مستقیم» حساب شود. مثال: «fratak.ir».</param>
    public static TrafficTouch Classify(string? query, string? referrer, string? ownHost)
    {
        var parameters = ParseQuery(query);
        var utmSource = Clean(parameters.GetValueOrDefault("utm_source"), 60);
        var utmMedium = Clean(parameters.GetValueOrDefault("utm_medium"), 30);
        var utmCampaign = Clean(parameters.GetValueOrDefault("utm_campaign"), 100);

        if (utmSource is not null)
            return new TrafficTouch(utmSource, utmMedium ?? "campaign", utmCampaign);
        if (parameters.ContainsKey("gclid"))
            return new TrafficTouch("google", "cpc", utmCampaign);

        var host = HostOf(referrer);
        if (host is null || IsOwnHost(host, ownHost))
            return utmCampaign is null ? TrafficTouch.Direct : new TrafficTouch(DirectSource, "campaign", utmCampaign);

        var (source, medium) = FromHost(host);
        return new TrafficTouch(source, utmMedium ?? medium, utmCampaign);
    }

    /// <summary>
    /// منبع از روی دامنه. مثال: «www.google.com» → (google, organic)؛ «blog.example.com» → (blog.example.com, referral).
    /// </summary>
    public static (string Source, string Medium) FromHost(string host)
    {
        host = host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host[4..];

        // google.com، google.co.uk، news.google.com … (ولی mail.google.com ایمیل است).
        if (!host.EndsWith("mail.google.com", StringComparison.Ordinal)
            && (host == "google.com" || host.StartsWith("google.", StringComparison.Ordinal) || host.Contains(".google.", StringComparison.Ordinal)
                || host.EndsWith(".google.com", StringComparison.Ordinal)))
            return ("google", "organic");

        foreach (var (known, source, medium) in KnownHosts)
        {
            if (host == known || host.EndsWith("." + known, StringComparison.Ordinal))
                return (source, medium);
        }
        return (Cut(host, 60), "referral");
    }

    /// <summary>
    /// نوع دستگاه از User-Agent. مثال: آیفون → mobile، آیپد → tablet، ویندوز → desktop.
    /// </summary>
    public static string Device(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return "desktop";
        if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase)
            || (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) && !userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)))
            return "tablet";
        if (userAgent.Contains("Mobi", StringComparison.OrdinalIgnoreCase) || userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
            return "mobile";
        return "desktop";
    }

    /// <summary>عنوان فارسی دستگاه.</summary>
    public static string DeviceTitle(string device) => device switch
    {
        "mobile" => "موبایل",
        "tablet" => "تبلت",
        _ => "رایانه",
    };

    /// <summary>
    /// ربات‌ها و ابزارهای خودکار شمرده نمی‌شوند (موتورهای جست‌وجو، پیش‌نمایش لینک پیام‌رسان‌ها، curl و …).
    /// </summary>
    public static bool IsBot(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent) || userAgent.Length < 20)
            return true;
        string[] markers = ["bot", "crawl", "spider", "slurp", "preview", "headless", "lighthouse", "curl", "wget", "python", "java/",
                            "httpclient", "okhttp", "go-http", "facebookexternalhit", "monitor", "scanner", "phantomjs", "selenium"];
        return markers.Any(m => userAgent.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>شناسهٔ بازدیدکننده باید دقیقاً ۳۲ نویسهٔ هگز کوچک باشد (همان که سرور ساخته).</summary>
    public static bool IsValidVisitorKey(string? key) => key is not null && VisitorKeyFormat.IsMatch(key);

    /// <summary>ساخت شناسهٔ تصادفی تازه برای کوکی fr_vid.</summary>
    public static string NewVisitorKey() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// تبدیل منبع به مقدار کوکی «آخرین منبع غیرمستقیم» (fr_src). مثال: (instagram, story, yalda) → «instagram|story|yalda».
    /// </summary>
    public static string EncodeTouch(TrafficTouch touch) =>
        string.Join('|', Uri.EscapeDataString(touch.Source), Uri.EscapeDataString(touch.Medium), Uri.EscapeDataString(touch.Campaign ?? ""));

    /// <summary>خواندن کوکی fr_src؛ مقدار دستکاری‌شده یا خراب → null.</summary>
    public static TrafficTouch? DecodeTouch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 400)
            return null;
        var parts = value.Split('|');
        if (parts.Length != 3)
            return null;
        try
        {
            var source = Clean(Uri.UnescapeDataString(parts[0]), 60);
            var medium = Clean(Uri.UnescapeDataString(parts[1]), 30);
            var campaign = Clean(Uri.UnescapeDataString(parts[2]), 100);
            return source is null || medium is null ? null : new TrafficTouch(source, medium, campaign);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    /// <summary>عنوان فارسی منبع. مثال: «google» → «گوگل»؛ ناشناخته همان نام دامنه.</summary>
    public static string SourceTitle(string? source) =>
        string.IsNullOrEmpty(source) ? "مستقیم" : SourceTitles.TryGetValue(source, out var title) ? title : source;

    /// <summary>عنوان فارسی نوع منبع. مثال: «organic» → «جست‌وجوی طبیعی».</summary>
    public static string MediumTitle(string? medium) =>
        string.IsNullOrEmpty(medium) ? "مستقیم" : MediumTitles.TryGetValue(medium, out var title) ? title : medium;

    /// <summary>
    /// ساخت لینک کمپین برای صفحهٔ «سازندهٔ لینک». پارامترهای قبلی آدرس حفظ می‌شوند و UTMهای قبلی جایگزین.
    /// مثال: («https://shop.ir/product/x»، instagram، story، yalda) → «https://shop.ir/product/x?utm_source=instagram&amp;utm_medium=story&amp;utm_campaign=yalda».
    /// </summary>
    /// <returns>لینک ساخته‌شده یا null اگر آدرس یا منبع نامعتبر بود.</returns>
    public static string? BuildCampaignUrl(string? url, string? source, string? medium, string? campaign, string? content = null)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            return null;
        var cleanSource = Clean(source, 60);
        if (cleanSource is null)
            return null;

        var kept = ParseQuery(uri.Query).Where(p => !p.Key.StartsWith("utm_", StringComparison.Ordinal))
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}");
        var utm = new List<string> { "utm_source=" + Uri.EscapeDataString(cleanSource) };
        if (Clean(medium, 30) is { } m)
            utm.Add("utm_medium=" + Uri.EscapeDataString(m));
        if (Clean(campaign, 100) is { } c)
            utm.Add("utm_campaign=" + Uri.EscapeDataString(c));
        if (Clean(content, 100) is { } ct)
            utm.Add("utm_content=" + Uri.EscapeDataString(ct));

        var builder = new UriBuilder(uri) { Query = string.Join('&', kept.Concat(utm)) };
        return builder.Uri.AbsoluteUri;
    }

    /// <summary>
    /// تمیز کردن مقدار UTM: حروف کوچک، ارقام فارسی به انگلیسی، فاصله → خط تیره، نویسه‌های غیرمجاز حذف.
    /// مثال: « Yalda ۱۴۰۵ » → «yalda-1405». خالی → null.
    /// </summary>
    public static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = SearchText.NormalizeDigits(value).Trim().ToLowerInvariant();
        text = Regex.Replace(text, @"\s+", "-");
        text = Unsafe.Replace(text, "");
        text = text.Trim('-', '.', '_');
        return text.Length == 0 ? null : Cut(text, max);
    }

    private static bool IsOwnHost(string host, string? ownHost)
    {
        if (string.IsNullOrWhiteSpace(ownHost))
            return false;
        static string Bare(string h) => h.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? h[4..] : h;
        return string.Equals(Bare(host), Bare(ownHost.Split(':')[0]), StringComparison.OrdinalIgnoreCase);
    }

    private static string? HostOf(string? referrer)
    {
        if (string.IsNullOrWhiteSpace(referrer) || !Uri.TryCreate(referrer.Trim(), UriKind.Absolute, out var uri))
            return null;
        // android-app://org.telegram.messenger/ → telegram (ارجاع از اپ اندروید).
        if (uri.Scheme == "android-app")
            return uri.Host.Contains("telegram", StringComparison.OrdinalIgnoreCase) ? "t.me"
                 : uri.Host.Contains("instagram", StringComparison.OrdinalIgnoreCase) ? "instagram.com"
                 : uri.Host.Contains("google", StringComparison.OrdinalIgnoreCase) ? "google.com"
                 : uri.Host;
        return uri.Scheme is "http" or "https" && uri.Host.Length > 0 ? uri.Host : null;
    }

    /// <summary>خواندن ساده و امن رشتهٔ پرس‌وجو (کلیدها با حروف کوچک؛ تکراری‌ها: اولی).</summary>
    private static Dictionary<string, string> ParseQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(query))
            return result;
        var text = query.TrimStart('?');
        if (text.Length > 2000)
            text = text[..2000];
        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = part.IndexOf('=');
            var rawKey = index < 0 ? part : part[..index];
            var rawValue = index < 0 ? "" : part[(index + 1)..];
            try
            {
                var key = Uri.UnescapeDataString(rawKey.Replace('+', ' ')).Trim().ToLowerInvariant();
                var value = Uri.UnescapeDataString(rawValue.Replace('+', ' '));
                if (key.Length > 0 && !result.ContainsKey(key))
                    result[key] = value;
            }
            catch (UriFormatException)
            {
                // پارامتر خراب نادیده گرفته می‌شود.
            }
        }
        return result;
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}
