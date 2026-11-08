using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>نمونهٔ آموزشی یک دسته: نام دسته و نام چند کالای فعلی آن.</summary>
public sealed record CategorySample(Guid Id, string Name, IReadOnlyList<string> ProductNames);

/// <summary>حدس دسته برای یک کالا. Score شباهت کسینوسی (۰ تا حدود ۱.۳ با امتیاز نام)، Margin فاصله با دستهٔ دوم.</summary>
public sealed record CategoryGuess(Guid CategoryId, double Score, double Margin);

/// <summary>یک خوشهٔ کالاهای مشابه که هنوز دسته ندارند: نام پیشنهادی دسته، نوع کالا (اگر شناخته شد) و اعضا.</summary>
public sealed record ProductCluster(string Name, ProductType? Type, IReadOnlyList<Guid> ProductIds);

/// <summary>
/// دسته‌بند داخلی کالاها (دور ۲۱) – بدون هیچ مدل زبانی یا سرویس بیرونی، فقط آمار واژه‌ها (TF-IDF).
/// <para>
/// روش: برای هر دسته یک «بردار مرکز» از واژه‌های نام کالاهای همان دسته ساخته می‌شود (نام خود دسته با وزن ۳).
/// واژه‌ای که در همهٔ دسته‌ها هست (مثل نام برند یا «مدل») وزن کمی می‌گیرد و واژهٔ خاص یک دسته (مثل «پاوربانک») وزن زیاد.
/// نام کالای تازه با همهٔ مرکزها مقایسه می‌شود و فقط وقتی دسته پذیرفته می‌شود که هم شباهت کافی باشد و هم با دستهٔ دوم
/// فاصلهٔ روشن داشته باشد (حدس مردد = بدون تغییر؛ کالا در «دسته‌بندی نشده» می‌ماند تا خوشه‌بندی یا ادمین تصمیم بگیرد).
/// </para>
/// <para>مثال: دسته‌های «پاوربانک» (۲۰ کالا) و «هدفون» (۳۰ کالا) ← «پاوربانک شیائومی ۲۰۰۰۰» = پاوربانک با امتیاز بالا.</para>
/// <para>محل استفاده: <see cref="SmartCategoryService"/> (درون‌ریزی و بازبینی دوره‌ای) و آزمون‌های واحد.</para>
/// </summary>
public sealed class CategoryClassifier
{
    /// <summary>کمترین شباهت پذیرفتنی.</summary>
    public const double MinScore = 0.30;

    /// <summary>کمترین فاصله با دستهٔ دوم.</summary>
    public const double MinMargin = 0.08;

    /// <summary>کمترین تعداد کالای مشابه برای ساخت دستهٔ تازه از «سایر» یا «دسته‌بندی نشده».</summary>
    public const int MinClusterSize = 3;

    private readonly List<(Guid Id, Dictionary<string, double> Vector, HashSet<string> NameTerms)> _centroids = [];
    private readonly Dictionary<string, double> _idf = new(StringComparer.Ordinal);

    private CategoryClassifier()
    {
    }

    /// <summary>تعداد دسته‌های آموزش‌دیده.</summary>
    public int Count => _centroids.Count;

    /// <summary>
    /// واژه‌های مفید یک نام: نرمال، بدون واژهٔ ایست، بدون عدد و مدل (A55 و 256 برای یک کالا خاص‌اند نه یک دسته)، ریشه‌یابی سبک.
    /// مثال: «هدفون‌های بی‌سیم سونی مدل WH-1000XM5» ← [هدفون، بی، سیم، سونی] (بی یک‌حرفی نیست پس می‌ماند).
    /// </summary>
    public static List<string> Terms(string? text) =>
        PersianText.Tokenize(text)
            .Where(t => t.Length >= 2 && !PersianText.IsStopWord(t) && !t.Any(char.IsDigit) && t != "مدل" && !ProductTypes.Fillers.Contains(t))
            .Select(PersianText.Stem)
            .ToList();

    /// <summary>
    /// ساخت دسته‌بند از نمونه‌ها. دستهٔ بدون کالا هم با نام خودش حضور دارد.
    /// مثال: Train([new(id1, "پاوربانک", ["پاوربانک انکر"]), new(id2, "هدفون", ["هدفون سونی"])]).
    /// </summary>
    public static CategoryClassifier Train(IEnumerable<CategorySample> samples)
    {
        var classifier = new CategoryClassifier();
        var docs = new List<(Guid Id, Dictionary<string, double> Counts, HashSet<string> NameTerms)>();
        foreach (var sample in samples)
        {
            var counts = new Dictionary<string, double>(StringComparer.Ordinal);
            var nameTerms = Terms(sample.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var term in nameTerms)
                counts[term] = counts.GetValueOrDefault(term) + 3;
            foreach (var name in sample.ProductNames.Take(300))
            {
                foreach (var term in Terms(name).Distinct())
                    counts[term] = counts.GetValueOrDefault(term) + 1;
            }
            if (counts.Count > 0)
                docs.Add((sample.Id, counts, nameTerms));
        }
        if (docs.Count == 0)
            return classifier;

        // IDF روی دسته‌ها: واژه‌ای که در همهٔ دسته‌ها آمده وزنش نزدیک صفر می‌شود.
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var doc in docs)
        {
            foreach (var term in doc.Counts.Keys)
                df[term] = df.GetValueOrDefault(term) + 1;
        }
        foreach (var (term, count) in df)
            classifier._idf[term] = Math.Log((1.0 + docs.Count) / (1.0 + count)) + 0.1;

        foreach (var doc in docs)
        {
            var total = doc.Counts.Values.Sum();
            var vector = doc.Counts.ToDictionary(p => p.Key, p => p.Value / total * classifier._idf[p.Key], StringComparer.Ordinal);
            Normalize(vector);
            classifier._centroids.Add((doc.Id, vector, doc.NameTerms));
        }
        return classifier;
    }

    /// <summary>
    /// حدس دسته برای یک نام کالا (یا null وقتی دسته‌ای آموزش ندیده یا واژهٔ مفیدی نیست).
    /// <para>اگر همهٔ واژه‌های نام دسته در چهار واژهٔ اول نام کالا باشد ۰.۳ امتیاز اضافه می‌گیرد («پاوربانک …» برای دستهٔ «پاوربانک»).</para>
    /// </summary>
    public CategoryGuess? Classify(string? title)
    {
        var terms = Terms(title);
        if (terms.Count == 0 || _centroids.Count == 0)
            return null;
        var query = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var term in terms.Distinct())
        {
            if (_idf.TryGetValue(term, out var idf))
                query[term] = idf;
        }
        if (query.Count == 0)
            return null;
        Normalize(query);
        var head = terms.Take(4).ToHashSet(StringComparer.Ordinal);

        var scores = _centroids.Select(c =>
        {
            var score = query.Sum(q => q.Value * c.Vector.GetValueOrDefault(q.Key));
            if (c.NameTerms.Count > 0 && c.NameTerms.All(head.Contains))
                score += 0.3;
            return (c.Id, Score: score);
        }).OrderByDescending(s => s.Score).ToList();
        var best = scores[0];
        var second = scores.Count > 1 ? scores[1].Score : 0;
        return new CategoryGuess(best.Id, Math.Round(best.Score, 4), Math.Round(best.Score - second, 4));
    }

    /// <summary>حدس مطمئن است؟ (شباهت ≥ ۰.۳۰ و فاصله با دومی ≥ ۰.۰۸).</summary>
    public static bool IsConfident(CategoryGuess? guess) => guess is { Score: >= MinScore, Margin: >= MinMargin };

    private static void Normalize(Dictionary<string, double> vector)
    {
        var length = Math.Sqrt(vector.Values.Sum(v => v * v));
        if (length <= 0)
            return;
        foreach (var key in vector.Keys.ToList())
            vector[key] /= length;
    }

    /// <summary>واژه‌هایی که هرگز نام دسته نمی‌شوند (صفت‌ها و واژه‌های عمومی فروشگاهی).</summary>
    private static readonly HashSet<string> NotAName = new(StringComparer.Ordinal)
    {
        "اورجینال", "اصل", "اصلی", "جدید", "طرح", "مدل", "رنگ", "سایز", "عدد", "عددی", "بسته", "ست", "پک", "کد", "نسخه", "سری", "سفید", "مشکی",
        "قرمز", "آبی", "سبز", "زرد", "صورتی", "طوسی", "نقره", "طلایی", "بزرگ", "کوچک", "متوسط", "مخصوص", "همراه", "دار", "ارزان", "لوکس", "فانتزی",
    };

    /// <summary>
    /// خوشه‌بندی کالاهای بی‌دسته برای ساخت دستهٔ تازه (فقط خوشه‌های ≥ <paramref name="minSize"/> کالا).
    /// <para>اول بر اساس «نوع کالا»ی شناخته‌شده؛ بعد بر اساس «سرواژه» (اولین واژهٔ معنادار فارسی نام که برند/صفت/عدد نیست)
    /// و اگر همهٔ اعضا واژهٔ دوم یکسان دارند نام دو واژه‌ای («لیوان سرامیکی»).</para>
    /// <para>مثال: «لیوان سرامیکی طرح گل»، «لیوان سرامیکی دسته‌دار»، «لیوان سرامیکی ساده» ← خوشهٔ «لیوان سرامیکی» با ۳ کالا.
    /// «سامسونگ X» و «سامسونگ Y» خوشه نمی‌شوند چون «سامسونگ» برند است.</para>
    /// </summary>
    public static IReadOnlyList<ProductCluster> Cluster(IEnumerable<(Guid Id, string Name)> products, IEnumerable<string> brandNames,
        int minSize = MinClusterSize)
    {
        var brands = brandNames.SelectMany(b => PersianText.Tokenize(b)).ToHashSet(StringComparer.Ordinal);
        var byType = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        var byHead = new Dictionary<string, List<(Guid Id, string? Second, string Surface)>>(StringComparer.Ordinal);
        foreach (var (id, name) in products)
        {
            if (ProductTypes.Detect(name) is { } type)
            {
                if (!byType.TryGetValue(type.Key, out var list))
                    byType[type.Key] = list = [];
                list.Add(id);
                continue;
            }
            var words = PersianText.Tokenize(name)
                .Where(w => !ProductTypes.Fillers.Contains(w) && !PersianText.IsStopWord(w))
                .ToList();
            var start = words.FindIndex(w => IsNameWord(w, brands));
            if (start < 0 || start > 1)
                continue;
            var second = start + 1 < words.Count && IsNameWord(words[start + 1], brands) ? words[start + 1] : null;
            var head = PersianText.Stem(words[start]);
            if (!byHead.TryGetValue(head, out var members))
                byHead[head] = members = [];
            members.Add((id, second, words[start]));
        }

        var clusters = new List<ProductCluster>();
        foreach (var (key, ids) in byType)
        {
            if (ids.Count >= minSize)
                clusters.Add(new ProductCluster(ProductTypes.Find(key)!.Name, ProductTypes.Find(key), ids));
        }
        foreach (var (head, members) in byHead)
        {
            if (members.Count < minSize)
                continue;
            var seconds = members.Select(m => m.Second).Distinct().ToList();
            // دور ۲۲: نام دسته از «شکل واقعی» واژه ساخته می‌شود نه ریشهٔ بریده‌شده (ریشه‌یاب «شکلات» را «شکل» می‌کند).
            // پرتکرارترین شکل نوشتاری اعضا انتخاب می‌شود. مثال: «شکلات تلخ …» ×۳ ← «شکلات تلخ» (نه «شکل تلخ»).
            var surface = members.GroupBy(m => m.Surface, StringComparer.Ordinal).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Length).First().Key;
            var name = seconds.Count == 1 && seconds[0] is { } shared ? surface + " " + shared : surface;
            clusters.Add(new ProductCluster(name, null, members.Select(m => m.Id).ToList()));
        }
        return clusters.OrderByDescending(c => c.ProductIds.Count).ToList();
    }

    /// <summary>
    /// دور ۲۲: واژهٔ صفت/رنگ/عمومی است (هرگز به‌تنهایی نام دسته نمی‌شود)؟ مثال: «مشکی» ← بله؛ «لیوان» ← خیر.
    /// <para>محل استفاده: <see cref="CategoryNames.Proper"/>.</para>
    /// </summary>
    public static bool IsAdjective(string word) => NotAName.Contains(word) || NotAName.Contains(PersianText.Stem(word));

    // واژهٔ قابل‌قبول برای نام دسته: فارسی، حداقل ۲ حرف، نه برند، نه صفت/رنگ، نه عدد.
    private static bool IsNameWord(string word, HashSet<string> brands) =>
        word.Length >= 2 && word.All(c => c is >= '\u0600' and <= '\u06FF') && !brands.Contains(word) && !NotAName.Contains(word)
        && !NotAName.Contains(PersianText.Stem(word));

    /// <summary>
    /// دسته‌های «سطل‌آشغالی» که کالایشان باید به دستهٔ دقیق‌تر برود: «سایر …»، «متفرقه»، «دسته‌بندی نشده»، Other/Misc.
    /// مثال: «سایر لوازم جانبی» ← بله؛ «لوازم جانبی» ← خیر.
    /// </summary>
    public static bool IsCatchAll(string? name, string? slug = null)
    {
        var n = PersianText.Normalize(name);
        var s = (slug ?? "").ToLowerInvariant();
        return n.StartsWith("سایر", StringComparison.Ordinal) || n.Contains("متفرقه", StringComparison.Ordinal)
               || n.Contains("دسته بندی نشده", StringComparison.Ordinal) || n.Contains("بدون دسته", StringComparison.Ordinal)
               || n is "other" or "others" or "misc" || s is "other" or "others" or "misc" or "uncategorized" || s.StartsWith("other-", StringComparison.Ordinal);
    }
}

/// <summary>
/// نیت جست‌وجو (دور ۲۱): عبارت «کلی» («گوشی»، «هدفون بی سیم») دسته را نشان می‌دهد و عبارت «دقیق» («گوشی A55»، «iPhone 15») خود کالا را.
/// <para>محل استفاده: صفحهٔ جست‌وجو (کارت «همهٔ مدل‌های این دسته» بالای نتیجه‌ها) و پیشنهادهای فوری جعبهٔ جست‌وجو.</para>
/// </summary>
public static class SearchIntent
{
    /// <summary>
    /// عبارت کلی است؟ بدون عدد/مدل و حداکثر سه واژه. مثال: «هدفون بی سیم» ← بله؛ «هدفون sony wh1000» ← خیر؛ «گوشی ۱۲۸ گیگ» ← خیر.
    /// </summary>
    public static bool IsBroad(string? query)
    {
        var words = PersianText.Tokenize(query);
        return words.Count is > 0 and <= 3 && !words.Any(w => w.Any(char.IsDigit));
    }

    /// <summary>
    /// دستهٔ متناظر با عبارت کلی (یا null). اول نام یکسان، بعد «نوع کالا»ی یکسان (گوشی ← «گوشی موبایل»)، بعد دسته‌ای که همهٔ واژه‌های عبارت در نامش هست.
    /// مثال: («گوشی»، [گوشی موبایل، قاب گوشی]) ← گوشی موبایل (نه قاب گوشی، چون نوع قاب از اول نامش «قاب» است).
    /// </summary>
    public static (Guid Id, string Name, string Slug)? MatchCategory(string? query, IEnumerable<(Guid Id, string Name, string Slug)> categories)
    {
        if (!IsBroad(query))
            return null;
        var list = categories.Where(c => !CategoryClassifier.IsCatchAll(c.Name, c.Slug)).ToList();
        var q = PersianText.Normalize(query);
        var exact = list.FirstOrDefault(c => PersianText.Normalize(c.Name) == q);
        if (exact.Slug is not null)
            return exact;
        if (ProductTypes.Detect(query) is { } type)
        {
            var typed = list.FirstOrDefault(c => ProductTypes.NameMatches(type, c.Name, c.Slug));
            if (typed.Slug is not null)
                return typed;
        }
        var terms = CategoryClassifier.Terms(query);
        if (terms.Count == 0)
            return null;
        var contains = list.Where(c =>
            {
                var head = CategoryClassifier.Terms(c.Name).Take(terms.Count + 1).ToHashSet(StringComparer.Ordinal);
                return terms.All(head.Contains);
            })
            .OrderBy(c => c.Name.Length)
            .FirstOrDefault();
        return contains.Slug is not null ? contains : null;
    }
}
