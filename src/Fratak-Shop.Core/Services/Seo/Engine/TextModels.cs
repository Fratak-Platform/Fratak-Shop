using System.Numerics;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  مدل‌های آماری متن (یادگیری ماشین سبک، کاملاً داخلی و بدون سرویس خارجی)
//  • TrigramLanguageModel : مدل زبانی سه‌کلمه‌ای که از متن‌های خود سایت (و صفحه‌های رقبا) یاد می‌گیرد و
//                           «روان و طبیعی بودن» یک جمله را می‌سنجد. نویسنده از بین چند جملهٔ هم‌معنی، روان‌ترین را
//                           (به سبک نوشتاری همین فروشگاه) انتخاب می‌کند.
//  • TfIdf                : اهمیت هر واژه در یک متن نسبت به کل سایت (برای پیدا کردن کلمهٔ کلیدی مقاله‌ها).
//  • TextRank             : خلاصه‌سازی استخراجی؛ مهم‌ترین جمله‌های یک مقاله را پیدا می‌کند (برای توضیحات متا).
//  • SimHash / Jaccard    : تشخیص متن تکراری یا تقریباً تکراری.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>توابع آماری کمکی.</summary>
public static class TextModels
{
    /// <summary>
    /// اثر انگشت ۶۴ بیتی متن (SimHash روی سه‌تایی‌های واژه). دو متن تقریباً یکسان اثر انگشت‌های نزدیک دارند.
    /// مثال: توضیحات کپی‌شده از سایت تأمین‌کننده روی دو محصول → فاصلهٔ همینگ ≤ ۳.
    /// </summary>
    public static ulong SimHash(string text)
    {
        var tokens = PersianText.Tokenize(text);
        if (tokens.Count == 0)
            return 0;

        Span<int> weights = stackalloc int[64];
        for (var i = 0; i < tokens.Count; i++)
        {
            var shingle = i + 2 < tokens.Count ? tokens[i] + " " + tokens[i + 1] + " " + tokens[i + 2] : tokens[i];
            var hash = Fnv1a(shingle);
            for (var bit = 0; bit < 64; bit++)
                weights[bit] += (hash >> bit & 1UL) == 1 ? 1 : -1;
        }

        ulong result = 0;
        for (var bit = 0; bit < 64; bit++)
            if (weights[bit] > 0)
                result |= 1UL << bit;
        return result;
    }

    /// <summary>تعداد بیت‌های متفاوت دو اثر انگشت (۰ = یکسان، ≤۳ = تقریباً تکراری).</summary>
    public static int Hamming(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    /// <summary>هش پایدار FNV-1a (برخلاف string.GetHashCode در هر اجرا یکسان است).</summary>
    public static ulong Fnv1a(string value)
    {
        var hash = 14695981039346656037UL;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    /// <summary>شباهت جاکارد دو مجموعه (۰ تا ۱).</summary>
    public static double Jaccard(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
            return 1;
        var setA = a as HashSet<string> ?? new HashSet<string>(a, StringComparer.Ordinal);
        var intersection = b.Distinct(StringComparer.Ordinal).Count(setA.Contains);
        var union = setA.Count + b.Distinct(StringComparer.Ordinal).Count() - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    /// <summary>
    /// خلاصه‌سازی TextRank: گرافی از جمله‌ها می‌سازد (وزن یال = اشتراک ریشهٔ واژه‌ها) و با PageRank مهم‌ترین
    /// جمله‌ها را پیدا می‌کند. خروجی: جمله‌ها به ترتیب اهمیت (بیشترین اول) همراه با امتیاز.
    /// مثال کاربرد: انتخاب جملهٔ اصلی یک مقاله برای توضیحات متا.
    /// </summary>
    public static List<(string Sentence, double Score, int Position)> TextRank(IReadOnlyList<string> sentences, int iterations = 30)
    {
        var n = sentences.Count;
        var result = new List<(string, double, int)>();
        if (n == 0)
            return result;

        var stems = sentences.Select(s => new HashSet<string>(PersianText.ContentStems(s), StringComparer.Ordinal)).ToList();
        var weights = new double[n, n];
        var outSum = new double[n];
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                if (stems[i].Count < 2 || stems[j].Count < 2)
                    continue;
                var common = stems[i].Count(stems[j].Contains);
                if (common == 0)
                    continue;
                // فرمول اصلی TextRank: اشتراک تقسیم بر لگاریتم طول دو جمله (جمله‌های بلند امتیاز بی‌جا نگیرند).
                var w = common / (Math.Log(stems[i].Count + 1) + Math.Log(stems[j].Count + 1));
                weights[i, j] = weights[j, i] = w;
                outSum[i] += w;
                outSum[j] += w;
            }
        }

        const double damping = 0.85;
        var scores = Enumerable.Repeat(1.0, n).ToArray();
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var next = new double[n];
            for (var i = 0; i < n; i++)
            {
                var sum = 0.0;
                for (var j = 0; j < n; j++)
                    if (weights[j, i] > 0 && outSum[j] > 0)
                        sum += weights[j, i] / outSum[j] * scores[j];
                next[i] = 1 - damping + damping * sum;
            }
            scores = next;
        }

        for (var i = 0; i < n; i++)
        {
            // جمله‌های اول متن معمولاً معرفی‌اند؛ کمی امتیاز بیشتر (روش رایج در خلاصه‌سازی خبر).
            var positionBoost = i < 3 ? 1.15 - i * 0.05 : 1.0;
            result.Add((sentences[i], scores[i] * positionBoost, i));
        }

        return result.OrderByDescending(r => r.Item2).ToList();
    }
}

/// <summary>
/// وزن TF-IDF واژه‌ها. «سند» = متن یک صفحه. واژه‌ای که در این متن زیاد و در کل سایت کم است، مهم‌تر است.
/// مثال: در مقالهٔ «راهنمای خرید هدفون» واژهٔ «هدفون» وزن بالا و واژهٔ «خرید» (در همهٔ صفحه‌ها) وزن کم دارد.
/// </summary>
public sealed class TfIdf
{
    private readonly Dictionary<string, int> _documentFrequency = new(StringComparer.Ordinal);

    public int Documents { get; private set; }

    /// <summary>افزودن یک سند به آمار (یک بار برای هر صفحهٔ سایت).</summary>
    public void Add(string text)
    {
        Documents++;
        foreach (var stem in PersianText.ContentStems(text).Distinct(StringComparer.Ordinal))
            _documentFrequency[stem] = _documentFrequency.GetValueOrDefault(stem) + 1;
    }

    /// <summary>IDF هموار‌شده: log((N+1)/(df+1)) + 1.</summary>
    public double Idf(string stem) => Math.Log((Documents + 1.0) / (_documentFrequency.GetValueOrDefault(stem) + 1.0)) + 1.0;

    /// <summary>مهم‌ترین ریشه‌های یک متن با وزن TF-IDF.</summary>
    public List<(string Stem, double Weight)> Top(string text, int count)
    {
        var stems = PersianText.ContentStems(text);
        if (stems.Count == 0)
            return [];
        return stems.GroupBy(s => s, StringComparer.Ordinal)
            .Select(g => (g.Key, (double)g.Count() / stems.Count * Idf(g.Key)))
            .OrderByDescending(x => x.Item2)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .Take(count)
            .ToList();
    }
}

/// <summary>
/// مدل زبانی سه‌کلمه‌ای (trigram) با درون‌یابی (interpolation) و هموارسازی ساده.
/// از متن‌های خود سایت یاد می‌گیرد؛ هرچه یک جمله به نوشته‌های واقعی فارسی سایت شبیه‌تر باشد امتیاز بیشتری می‌گیرد.
/// استفاده: <see cref="ContentWriter"/> از بین چند جملهٔ هم‌معنی، جمله‌ای با بیشترین <see cref="Fluency"/> را انتخاب می‌کند.
/// مثال: بعد از آموزش روی توضیحات محصولات، «ارسال سریع به سراسر کشور» امتیاز بیشتری از «سراسر کشور ارسال سریع به» می‌گیرد.
/// </summary>
public sealed class TrigramLanguageModel
{
    private readonly Dictionary<string, int> _unigrams = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _bigrams = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _trigrams = new(StringComparer.Ordinal);
    private long _total;
    private int _vocabulary;

    // وزن‌های درون‌یابی (مجموع ۱). مقدارهای رایج در ادبیات برای پیکرهٔ کوچک.
    private const double L3 = 0.5, L2 = 0.3, L1 = 0.15, L0 = 0.05;
    private const string Start = "<s>", End = "</s>";

    /// <summary>تعداد واژه‌های آموزش‌دیده.</summary>
    public long TokenCount => _total;

    /// <summary>آموزش با یک متن (به جمله‌ها شکسته می‌شود). برای کل سایت چند ثانیه هم طول نمی‌کشد.</summary>
    public void Train(string? text)
    {
        foreach (var sentence in PersianText.Sentences(text))
        {
            var tokens = PersianText.Tokenize(sentence);
            if (tokens.Count < 2)
                continue;
            var padded = new List<string>(tokens.Count + 3) { Start, Start };
            padded.AddRange(tokens);
            padded.Add(End);
            for (var i = 2; i < padded.Count; i++)
            {
                var seen = _unigrams.GetValueOrDefault(padded[i]);
                if (seen == 0)
                    _vocabulary++;
                _unigrams[padded[i]] = seen + 1;
                _total++;
                var bigram = padded[i - 1] + " " + padded[i];
                _bigrams[bigram] = _bigrams.GetValueOrDefault(bigram) + 1;
                var trigram = padded[i - 2] + " " + bigram;
                _trigrams[trigram] = _trigrams.GetValueOrDefault(trigram) + 1;
            }
            // شمارش زمینه‌ها برای مخرج کسرها
            for (var i = 1; i < padded.Count - 1; i++)
            {
                var context = "|" + padded[i - 1] + " " + padded[i];
                _bigrams[context] = _bigrams.GetValueOrDefault(context) + 1;
                var single = "|" + padded[i];
                _unigrams[single] = _unigrams.GetValueOrDefault(single) + 1;
            }
        }
    }

    /// <summary>
    /// میانگین لگاریتم احتمال هر واژه (عددی منفی؛ نزدیک‌تر به صفر = روان‌تر). جملهٔ خالی → منفی بی‌نهایت نمی‌دهد، ۲۰- می‌دهد.
    /// </summary>
    public double Fluency(string sentence)
    {
        var tokens = PersianText.Tokenize(sentence);
        if (tokens.Count == 0)
            return -20;

        var vocabulary = Math.Max(1, _vocabulary);
        var padded = new List<string>(tokens.Count + 3) { Start, Start };
        padded.AddRange(tokens);
        padded.Add(End);

        var logSum = 0.0;
        for (var i = 2; i < padded.Count; i++)
        {
            var w = padded[i];
            var p1 = _total == 0 ? 0 : (double)_unigrams.GetValueOrDefault(w) / _total;
            var c1 = _unigrams.GetValueOrDefault("|" + padded[i - 1]);
            var p2 = c1 == 0 ? 0 : (double)_bigrams.GetValueOrDefault(padded[i - 1] + " " + w) / c1;
            var c2 = _bigrams.GetValueOrDefault("|" + padded[i - 2] + " " + padded[i - 1]);
            var p3 = c2 == 0 ? 0 : (double)_trigrams.GetValueOrDefault(padded[i - 2] + " " + padded[i - 1] + " " + w) / c2;
            var p = L3 * p3 + L2 * p2 + L1 * p1 + L0 / (vocabulary + 1);
            logSum += Math.Log(p);
        }

        return logSum / (padded.Count - 2);
    }
}
