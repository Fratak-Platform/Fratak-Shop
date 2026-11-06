using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>خلاصهٔ نظر خریداران دربارهٔ یک جنبهٔ محصول (مثلاً «باتری»).</summary>
/// <param name="Aspect">نام جنبه.</param>
/// <param name="Positive">تعداد اشارهٔ مثبت.</param>
/// <param name="Negative">تعداد اشارهٔ منفی.</param>
/// <param name="Quote">یک جملهٔ واقعی کوتاه از دیدگاه‌ها (بدون نام).</param>
public sealed record ReviewAspect(string Aspect, int Positive, int Negative, string? Quote);

/// <summary>یک محصول برای تحلیل‌های قیمت و مقایسه.</summary>
public sealed record InsightProduct(Guid Id, string Name, string Slug, decimal Price, bool InStock, int Sales, Guid? BrandId,
    IReadOnlyDictionary<string, string> Features);

/// <summary>یک بازهٔ قیمت برای صفحهٔ «بهترین ... زیر ... تومان».</summary>
public sealed record PriceBand(decimal Max, int Count, string Label);

/// <summary>
/// تحلیل‌های محتوایی از دادهٔ واقعی فروشگاه (موارد ۱۰۴، ۱۰۷، ۱۹۰، ۱۹۴ و ۱۹۵ پیشنهاد سئو).
/// <para>اصل: هیچ متنی ساختگی نیست؛ همه از دیدگاه‌های واقعی، قیمت‌ها و مشخصات ثبت‌شده ساخته می‌شود. همین «تجربه و داده»
/// (E-E-A-T) است که صفحه را از رقبا متمایز می‌کند.</para>
/// <para>کجا استفاده می‌شود؟ بخش «خلاصهٔ نظر خریداران» صفحهٔ محصول، صفحه‌های فرود قیمت و مقایسه.</para>
/// </summary>
public static class ContentInsights
{
    private static readonly (string Aspect, string[] Words)[] Aspects =
    [
        ("کیفیت صدا", ["صدا", "صدای", "بیس", "باس", "میکروفون"]),
        ("باتری و شارژ", ["باتری", "شارژ", "شارژدهی"]),
        ("کیفیت ساخت", ["جنس", "بدنه", "ساخت", "کیفیت"]),
        ("ارزش خرید", ["قیمت", "ارزش", "ارزون", "ارزان", "گران", "گرون"]),
        ("ارسال و بسته‌بندی", ["ارسال", "بسته", "پیک", "تحویل"]),
        ("راحتی", ["راحت", "سبک", "سنگین", "اندازه", "سایز"]),
        ("اتصال", ["بلوتوث", "اتصال", "قطع", "وصل"]),
        ("صفحه‌نمایش", ["صفحه", "نمایشگر", "تصویر"]),
        ("سرعت", ["سرعت", "سریع", "کند"]),
    ];

    private static readonly HashSet<string> PositiveWords = new(StringComparer.Ordinal)
    {
        "عالی", "خوب", "خوبه", "راضی", "قوی", "سریع", "ممتاز", "العاده", "عالیه", "محشر", "باکیفیت", "راحت", "سبک",
        "ارزان", "ارزون", "تمیز", "شفاف", "دقیق", "پیشنهاد",
    };

    private static readonly HashSet<string> NegativeWords = new(StringComparer.Ordinal)
    {
        "بد", "ضعیف", "خراب", "کم", "قطع", "ناراضی", "افتضاح", "کند", "داغ", "سنگین", "گران", "گرون", "دیر", "شکسته", "ایراد", "مشکل",
    };

    // واژه‌ها به شکل «نرمال‌شده» نوشته شده‌اند (PersianText.Tokenize نیم‌فاصله را فاصله می‌کند؛ «فوق‌العاده» ← «فوق» + «العاده»).
    private static readonly HashSet<string> Negations = new(StringComparer.Ordinal) { "نیست", "نبود", "نه", "اصلا", "نداره", "ندارد" };

    /// <summary>
    /// خلاصهٔ جنبه‌ها از دیدگاه‌های تأییدشده. کمتر از ۳ دیدگاه ← فهرست خالی (خلاصهٔ دو نظر گمراه‌کننده است).
    /// فقط جنبه‌هایی که حداقل ۲ بار اشاره شده‌اند. مثال: «باتری عالیه ولی صدا ضعیفه» ← باتری +۱، کیفیت صدا −۱.
    /// </summary>
    public static List<ReviewAspect> SummarizeReviews(IEnumerable<string> reviews)
    {
        var list = reviews.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        if (list.Count < 3)
            return [];

        var tally = new Dictionary<string, (int Pos, int Neg, string? Quote)>(StringComparer.Ordinal);
        foreach (var review in list)
        {
            foreach (var sentence in SplitClauses(review))
            {
                var tokens = PersianText.Tokenize(sentence);
                if (tokens.Count == 0)
                    continue;
                var sentiment = Sentiment(tokens);
                if (sentiment == 0)
                    continue;
                foreach (var (aspect, words) in Aspects)
                {
                    if (!tokens.Any(t => words.Contains(t)))
                        continue;
                    var old = tally.TryGetValue(aspect, out var v) ? v : (0, 0, null);
                    var quote = old.Quote ?? (sentence.Trim().Length is >= 12 and <= 120 ? sentence.Trim() : null);
                    tally[aspect] = sentiment > 0 ? (old.Pos + 1, old.Neg, quote) : (old.Pos, old.Neg + 1, quote);
                }
            }
        }

        return tally
            .Where(t => t.Value.Pos + t.Value.Neg >= 2)
            .OrderByDescending(t => t.Value.Pos + t.Value.Neg)
            .Take(6)
            .Select(t => new ReviewAspect(t.Key, t.Value.Pos, t.Value.Neg, t.Value.Quote))
            .ToList();
    }

    /// <summary>جهت احساس یک بند: ۱ مثبت، ۱- منفی، ۰ خنثی. «خوب نیست» منفی است.</summary>
    public static int Sentiment(IReadOnlyList<string> tokens)
    {
        var score = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var value = PositiveWords.Contains(tokens[i]) ? 1 : NegativeWords.Contains(tokens[i]) ? -1 : 0;
            if (value == 0)
                continue;
            var negated = Enumerable.Range(i + 1, 2).Any(j => j < tokens.Count && Negations.Contains(tokens[j]))
                          || i > 0 && Negations.Contains(tokens[i - 1]);
            score += negated ? -value : value;
        }

        return Math.Sign(score);
    }

    private static IEnumerable<string> SplitClauses(string text) =>
        PersianText.Sentences(text)
            .SelectMany(s => s.Split([" ولی ", " اما ", " ولیکن ", " ؛", "؛", " but "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// بازه‌های قیمت معنادار برای یک دسته: سقف‌های «گرد» (مثل ۵۰۰ هزار، ۱ میلیون، ۲ میلیون) که حداقل ۴ محصول موجود زیرشان
    /// هست و حداقل ۲۵٪ محصولات را کنار می‌گذارند (وگرنه صفحه با خود دسته فرقی ندارد = محتوای تکراری).
    /// قیمت‌ها به تومان. مثال: قیمت‌های ۳۰۰ تا ۴ میلیون ← [«زیر ۱ میلیون تومان»، «زیر ۲ میلیون تومان»].
    /// </summary>
    public static List<PriceBand> PriceBands(IReadOnlyList<decimal> prices, int max = 3)
    {
        var valid = prices.Where(p => p > 0).OrderBy(p => p).ToList();
        var result = new List<PriceBand>();
        if (valid.Count < 6)
            return result;
        decimal[] steps = [200_000, 300_000, 500_000, 1_000_000, 1_500_000, 2_000_000, 3_000_000, 5_000_000, 10_000_000, 20_000_000, 30_000_000, 50_000_000, 100_000_000];
        var lastCount = 0;
        foreach (var step in steps)
        {
            var count = valid.Count(p => p <= step);
            if (count < 4 || count > valid.Count * 0.75 || count - lastCount < 2)
                continue;
            result.Add(new PriceBand(step, count, "زیر " + MoneyLabel(step)));
            lastCount = count;
        }

        return result.TakeLast(max).ToList();
    }

    /// <summary>برچسب گرد مبلغ. مثال: ۱۵۰۰۰۰۰ ← «۱٫۵ میلیون تومان»، ۵۰۰۰۰۰ ← «۵۰۰ هزار تومان».</summary>
    public static string MoneyLabel(decimal toman) =>
        toman >= 1_000_000
            ? PersianText.Decimal1((double)(toman / 1_000_000m)) + " میلیون تومان"
            : PersianText.Digits((long)(toman / 1000)) + " هزار تومان";

    /// <summary>
    /// جفت‌های معنادار برای صفحهٔ «مقایسه» (مورد ۱۹۵): هر دو موجود، قیمت نزدیک (اختلاف حداکثر ۳۵٪)، حداقل ۳ ویژگی مشترک
    /// با مقدار متفاوت یا برابر (تا جدول مقایسه پر باشد). محبوب‌ترها اول؛ هر محصول حداکثر در ۲ جفت.
    /// </summary>
    public static List<(InsightProduct A, InsightProduct B, int Shared)> ComparisonPairs(IReadOnlyList<InsightProduct> products, int max = 5)
    {
        var candidates = products.Where(p => p.InStock && p.Price > 0 && p.Features.Count >= 3)
            .OrderByDescending(p => p.Sales).Take(30).ToList();
        var pairs = new List<(InsightProduct A, InsightProduct B, int Shared, double Rank)>();
        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                var a = candidates[i];
                var b = candidates[j];
                var ratio = (double)(Math.Max(a.Price, b.Price) / Math.Min(a.Price, b.Price));
                if (ratio > 1.35)
                    continue;
                var shared = a.Features.Keys.Count(k => b.Features.ContainsKey(k));
                if (shared < 3)
                    continue;
                pairs.Add((a, b, shared, a.Sales + b.Sales + shared * 2 - (ratio - 1) * 10));
            }
        }

        var usage = new Dictionary<Guid, int>();
        var result = new List<(InsightProduct, InsightProduct, int)>();
        foreach (var pair in pairs.OrderByDescending(p => p.Rank))
        {
            if (result.Count >= max)
                break;
            if (usage.GetValueOrDefault(pair.A.Id) >= 2 || usage.GetValueOrDefault(pair.B.Id) >= 2)
                continue;
            usage[pair.A.Id] = usage.GetValueOrDefault(pair.A.Id) + 1;
            usage[pair.B.Id] = usage.GetValueOrDefault(pair.B.Id) + 1;
            result.Add((pair.A, pair.B, pair.Shared));
        }

        return result;
    }
}
