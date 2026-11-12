using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>نیت جست‌وجو (مورد ۱۸۰).</summary>
public enum SearchIntent : byte
{
    /// <summary>نامشخص.</summary>
    None = 0,

    /// <summary>قصد خرید: «خرید»، «قیمت»، «سفارش» ← صفحهٔ محصول/دسته.</summary>
    Transactional = 1,

    /// <summary>مقایسه/بررسی پیش از خرید: «بهترین»، «مقایسه»، «یا»، «بررسی» ← مقاله/صفحهٔ مقایسه.</summary>
    Commercial = 2,

    /// <summary>اطلاعاتی: «چیست»، «چگونه»، «آموزش» ← مقاله.</summary>
    Informational = 3,

    /// <summary>ناوبری: نام سایت/برند خودمان.</summary>
    Navigational = 4,
}

/// <summary>یک کلمهٔ کلیدی با امتیاز و منبع داده (برای نمایش «از کجا آمد»).</summary>
public sealed record KeywordSignal(string Phrase, double Score, string Source);

/// <summary>دو یا چند صفحه که روی یک جست‌وجو با هم رقابت می‌کنند (Cannibalization، مورد ۱۸۲).</summary>
public sealed record CannibalGroup(string Query, IReadOnlyList<(string Path, int Impressions, double Position)> Pages);

/// <summary>
/// هوش کلمات کلیدی (موارد ۱۷۷ تا ۱۸۸ پیشنهاد سئو). همه رایگان و از دادهٔ واقعی:
/// جست‌وجوهای سرچ کنسول همان صفحه، جست‌وجوهای داخلی سایت، پیشنهادهای گوگل/بینگ (تحقیق موتور سئو) و مشخصات محصول.
/// <para>کجا استفاده می‌شود؟ پر کردن خودکار «کلمات کلیدی متا» و «برچسب‌های مقاله»، گزارش رقابت صفحه‌ها، تحلیل شکاف محتوا.</para>
/// </summary>
public static class KeywordIntel
{
    private static readonly string[] TransactionalWords = ["خرید", "قیمت", "سفارش", "فروش", "ارزان", "تخفیف", "ارسال", "اقساط", "buy", "price"];
    private static readonly string[] CommercialWords = ["بهترین", "مقایسه", "بررسی", "نقد", "یا", "بهتره", "vs", "مشخصات", "معرفی", "best", "review"];
    private static readonly string[] InformationalWords = ["چیست", "چیه", "چگونه", "چطور", "آموزش", "روش", "راهنما", "علت", "چرا", "تفاوت", "how", "what"];

    /// <summary>
    /// تشخیص نیت جست‌وجو. مثال: «قیمت هدفون سونی» ← Transactional، «بهترین هدفون ۱۴۰۵» ← Commercial،
    /// «هدفون نویز کنسلینگ چیست» ← Informational، «فراتک» (نام سایت) ← Navigational.
    /// </summary>
    public static SearchIntent Intent(string? query, string? siteName = null)
    {
        var tokens = PersianText.Tokenize(query);
        if (tokens.Count == 0)
            return SearchIntent.None;
        if (!string.IsNullOrWhiteSpace(siteName) && PersianText.Tokenize(siteName).Any(t => t.Length > 2 && tokens.Contains(t)))
            return SearchIntent.Navigational;
        if (tokens.Any(t => InformationalWords.Contains(t)))
            return SearchIntent.Informational;
        if (tokens.Any(t => TransactionalWords.Contains(t)))
            return SearchIntent.Transactional;
        if (tokens.Any(t => CommercialWords.Contains(t)))
            return SearchIntent.Commercial;
        // فقط نام محصول/نوع (مثل «هدفون سونی») در فروشگاه معمولاً قصد خرید است.
        return SearchIntent.Transactional;
    }

    /// <summary>
    /// انتخاب کلمات کلیدی یک صفحه از منبع‌های واقعی با وزن: سرچ کنسول (×۳، چون ثابت می‌کند گوگل صفحه را برای آن نشان داده)،
    /// جست‌وجوی داخلی (×۲)، تحقیق رقبا/پیشنهاد گوگل (×۱). عبارت‌های بی‌ربط (بدون هیچ واژهٔ مشترک با نام صفحه) کنار می‌روند.
    /// <example><code>
    /// KeywordIntel.Rank("هدفون سونی WH-1000XM5", gsc: [("خرید هدفون سونی", 420)], internal: [("هدفون سونی", 30)], research: ["هدفون سونی xm5"], max: 6)
    /// </code></example>
    /// </summary>
    public static List<KeywordSignal> Rank(string pageName, IEnumerable<(string Query, int Impressions)>? gsc,
        IEnumerable<(string Query, int Count)>? internalSearch, IEnumerable<string>? research, int max = 8)
    {
        var nameTokens = PersianText.ContentStems(pageName).ToHashSet(StringComparer.Ordinal);
        var scores = new Dictionary<string, (double Score, string Source, string Phrase)>(StringComparer.Ordinal);

        void Add(string phrase, double score, string source)
        {
            var clean = PersianText.Clean(phrase);
            if (clean.Length < 3 || clean.Length > 60 || PersianText.WordCount(clean) > 7)
                return;
            var stems = PersianText.ContentStems(clean);
            if (nameTokens.Count > 0 && !stems.Any(nameTokens.Contains))
                return;
            var key = PersianText.Normalize(clean);
            scores[key] = scores.TryGetValue(key, out var old)
                ? (old.Score + score, old.Source, old.Phrase)
                : (score, source, clean);
        }

        foreach (var (query, impressions) in gsc ?? [])
            Add(query, 3 * Math.Log10(10 + Math.Max(0, impressions)), "سرچ کنسول");
        foreach (var (query, count) in internalSearch ?? [])
            Add(query, 2 * Math.Log10(10 + Math.Max(0, count)), "جست‌وجوی سایت");
        var rank = 0;
        foreach (var phrase in research ?? [])
            Add(phrase, Math.Max(0.4, 1.2 - rank++ * 0.05), "پیشنهاد گوگل/بینگ");

        return scores.Values
            .OrderByDescending(v => v.Score)
            .Select(v => new KeywordSignal(v.Phrase, Math.Round(v.Score, 2), v.Source))
            .Take(Math.Max(1, max))
            .ToList();
    }

    /// <summary>
    /// تشخیص رقابت صفحه‌ها روی یک جست‌وجو: هر جست‌وجو که ۲ صفحه یا بیشتر هر کدام حداقل ۲۰٪ نمایش‌هایش را گرفته‌اند
    /// (و مجموع نمایش ≥ ۱۰۰). مرتب به ترتیب نمایش.
    /// </summary>
    public static List<CannibalGroup> Cannibalization(IEnumerable<(string Query, string Path, int Impressions, double Position)> rows)
    {
        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Query) && !string.IsNullOrWhiteSpace(r.Path))
            .GroupBy(r => PersianText.Normalize(r.Query), StringComparer.Ordinal)
            .Select(g =>
            {
                var total = g.Sum(r => r.Impressions);
                var pages = g.GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(p => (Path: p.Key, Impressions: p.Sum(x => x.Impressions), Position: p.Average(x => x.Position)))
                    .Where(p => total > 0 && p.Impressions >= total * 0.2)
                    .OrderByDescending(p => p.Impressions)
                    .ToList();
                return (Query: g.First().Query, Total: total, Pages: pages);
            })
            .Where(x => x.Total >= 100 && x.Pages.Count >= 2)
            .OrderByDescending(x => x.Total)
            .Select(x => new CannibalGroup(x.Query, x.Pages))
            .ToList();
    }

    /// <summary>
    /// گونه‌های نگارشی یک عبارت (مورد ۱۸۴): با/بی نیم‌فاصله و فاصله، ی/ک عربی. برای فهم جست‌وجوی داخلی و کلمات کلیدی متا.
    /// مثال: «هندزفری» ← «هندز فری»؛ «گوشی‌ها» ← «گوشی ها»، «گوشیها».
    /// </summary>
    public static List<string> Variants(string phrase)
    {
        var clean = PersianText.Clean(phrase);
        var set = new HashSet<string>(StringComparer.Ordinal) { clean };
        if (clean.Contains('\u200C'))
        {
            set.Add(clean.Replace('\u200C', ' '));
            set.Add(clean.Replace("\u200C", "", StringComparison.Ordinal));
        }

        foreach (var (joined, split) in new[] { ("هندزفری", "هندز فری"), ("لپتاپ", "لپ تاپ"), ("لپ‌تاپ", "لپ تاپ"), ("پاوربانک", "پاور بانک"), ("کیبورد", "کی بورد"), ("ایرپاد", "ایر پاد") })
        {
            if (clean.Contains(joined, StringComparison.Ordinal))
                set.Add(clean.Replace(joined, split, StringComparison.Ordinal));
            if (clean.Contains(split, StringComparison.Ordinal))
                set.Add(clean.Replace(split, joined, StringComparison.Ordinal));
        }

        set.Remove("");
        return set.ToList();
    }

    /// <summary>
    /// عبارت‌های دم‌بلند از مشخصات واقعی (مورد ۱۸۳): «{نوع} {برند}»، «{نوع} {مقدار ویژگی}»، «قیمت {نوع} {برند}».
    /// مثال: («هدفون»، «سونی»، ["بلوتوثی","نویز کنسلینگ"]) ← «هدفون سونی»، «هدفون بلوتوثی»، «هدفون نویز کنسلینگ».
    /// </summary>
    public static List<string> LongTails(string? typeWord, string? brand, IEnumerable<string>? featureValues, int max = 6)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(typeWord))
            return result;
        var type = PersianText.Clean(typeWord);
        if (!string.IsNullOrWhiteSpace(brand))
        {
            result.Add($"{type} {PersianText.Clean(brand)}");
            result.Add($"قیمت {type} {PersianText.Clean(brand)}");
        }

        foreach (var value in (featureValues ?? []).Select(PersianText.Clean)
                     .Where(v => v.Length is >= 3 and <= 25 && PersianText.PersianRatio(v) > 0.5 && !v.Any(char.IsDigit)))
            result.Add($"{type} {value}");
        return result.Distinct(StringComparer.Ordinal).Take(max).ToList();
    }
}
