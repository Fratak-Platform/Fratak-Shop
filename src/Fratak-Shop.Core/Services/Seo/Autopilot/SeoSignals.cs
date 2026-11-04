namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>یک ربات شناخته‌شده.</summary>
/// <param name="Name">نام کوتاه (مثال: «Googlebot»).</param>
/// <param name="DnsSuffixes">پسوند نام میزبان معکوس (Reverse DNS) آی‌پی واقعی این ربات؛ خالی = قابل تأیید نیست.</param>
/// <param name="IsSearchEngine">ربات موتور جست‌وجو (در گزارش «خزش گوگل/بینگ» مهم است).</param>
public sealed record KnownBot(string Name, string[] DnsSuffixes, bool IsSearchEngine);

/// <summary>
/// تشخیص ربات‌ها از User-Agent (مورد ۷۷ پیشنهاد سئو: لاگ ربات‌ها). تأیید «واقعی بودن» گوگل‌بات با DNS معکوس در لایهٔ وب
/// انجام می‌شود (روش رسمی گوگل: آی‌پی ← نام میزبان googlebot.com/google.com ← دوباره آی‌پی).
/// </summary>
public static class SeoBots
{
    private static readonly (string Token, KnownBot Bot)[] Bots =
    [
        ("googlebot", new("Googlebot", [".googlebot.com", ".google.com", ".googleusercontent.com"], true)),
        ("google-inspectiontool", new("Googlebot", [".googlebot.com", ".google.com"], true)),
        ("adsbot-google", new("Google-Ads", [".googlebot.com", ".google.com"], true)),
        ("mediapartners-google", new("Google-Ads", [".googlebot.com", ".google.com"], true)),
        ("bingbot", new("Bingbot", [".search.msn.com"], true)),
        ("yandex", new("YandexBot", [".yandex.ru", ".yandex.net", ".yandex.com"], true)),
        ("duckduckbot", new("DuckDuckBot", [], true)),
        ("applebot", new("Applebot", [".applebot.apple.com"], true)),
        ("torob", new("TorobBot", [], true)),
        ("emalls", new("EmallsBot", [], true)),
        ("baiduspider", new("Baiduspider", [".baidu.com", ".baidu.jp"], true)),
        ("petalbot", new("PetalBot", [".petalsearch.com"], true)),
        ("ahrefsbot", new("AhrefsBot", [], false)),
        ("semrushbot", new("SemrushBot", [], false)),
        ("mj12bot", new("MJ12bot", [], false)),
        ("dotbot", new("DotBot", [], false)),
        ("gptbot", new("GPTBot", [], false)),
        ("claudebot", new("ClaudeBot", [], false)),
        ("bytespider", new("Bytespider", [], false)),
        ("facebookexternalhit", new("Facebook", [], false)),
        ("telegrambot", new("Telegram", [], false)),
        ("whatsapp", new("WhatsApp", [], false)),
        ("twitterbot", new("Twitter", [], false)),
    ];

    /// <summary>ربات از روی User-Agent؛ مرورگر معمولی ← null. مثال: «Mozilla/5.0 (compatible; Googlebot/2.1; …)» ← Googlebot.</summary>
    public static KnownBot? Detect(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return null;
        var ua = userAgent.ToLowerInvariant();
        foreach (var (token, bot) in Bots)
        {
            if (ua.Contains(token, StringComparison.Ordinal))
                return bot;
        }

        return null;
    }

    /// <summary>آیا نام میزبان معکوس با پسوندهای رسمی ربات می‌خواند؟ (بدون پسوند تعریف‌شده ← null یعنی «قابل تأیید نیست»)</summary>
    public static bool? HostMatches(KnownBot bot, string? hostName)
    {
        if (bot.DnsSuffixes.Length == 0)
            return null;
        if (string.IsNullOrWhiteSpace(hostName))
            return false;
        var host = hostName.TrimEnd('.').ToLowerInvariant();
        return bot.DnsSuffixes.Any(s => host.EndsWith(s, StringComparison.Ordinal));
    }
}

/// <summary>آستانه‌های رسمی Core Web Vitals گوگل (web.dev): «خوب» تا Good و «ضعیف» از Poor به بالا.</summary>
public static class SeoVitals
{
    /// <summary>معیارهای پذیرفته‌شده و آستانه‌ها (LCP/INP/FCP/TTFB میلی‌ثانیه، CLS بدون واحد).</summary>
    public static readonly IReadOnlyDictionary<string, (double Good, double Poor)> Thresholds = new Dictionary<string, (double, double)>(StringComparer.Ordinal)
    {
        ["LCP"] = (2500, 4000),
        ["INP"] = (200, 500),
        ["CLS"] = (0.1, 0.25),
        ["FCP"] = (1800, 3000),
        ["TTFB"] = (800, 1800),
    };

    /// <summary>نوع‌های صفحه که اسکریپت سنجش می‌فرستد.</summary>
    public static readonly HashSet<string> PageTypes = new(StringComparer.Ordinal) { "home", "product", "category", "blog", "post", "search", "page", "other" };

    /// <summary>مقدار معقول است؟ (حفاظت در برابر دادهٔ جعلی/خراب: منفی، بی‌نهایت، بزرگ‌تر از ۶۰ ثانیه)</summary>
    public static bool IsValid(string metric, double value) =>
        Thresholds.ContainsKey(metric) && !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0
        && value <= (metric == "CLS" ? 10 : 60_000);

    /// <summary>صدک ۷۵ (معیار رسمی گوگل برای گزارش CWV). فهرست خالی ← ۰.</summary>
    public static double P75(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
            return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var rank = 0.75 * (sorted.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
    }

    /// <summary>وضعیت: ۱ خوب، ۰ نیاز به بهبود، ۱- ضعیف.</summary>
    public static int Rate(string metric, double value)
    {
        if (!Thresholds.TryGetValue(metric, out var t))
            return 0;
        return value <= t.Good ? 1 : value > t.Poor ? -1 : 0;
    }
}
