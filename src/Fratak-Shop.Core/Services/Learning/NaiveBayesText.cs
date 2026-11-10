using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Learning;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۴ – یادگیری ماشین درون‌برنامه‌ای (بدون کتابخانهٔ بومی و بدون سرویس بیرونی).
//  چرا ML.NET نه؟ کتابخانه‌های بومی آن روی هاست اشتراکی (محدودیت W^X) ممکن است بالا نیاید و حجم انتشار را چند ده مگابایت
//  بزرگ می‌کند. این طبقه‌بند «بیز ساده» (Multinomial Naive Bayes) در چند صد خط C# همان کار را برای متن کوتاه فارسی انجام می‌دهد:
//  آموزش در چند میلی‌ثانیه، پیش‌بینی در چند میکروثانیه، بدون حافظهٔ زیاد.
//  ویژگی‌ها: واژه‌ها + جفت‌واژه‌های پشت‌سرهم + سه‌حرفی‌های داخل هر واژه (تا غلط تایپی و پسوندها هم شناخته شوند).
//  محل استفاده: AssistantIntentModel (تشخیص نیت پیام دستیار) و طبقه‌بند دسته‌بندی کالا.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک پیش‌بینی: برچسب و احتمال (۰ تا ۱).</summary>
public readonly record struct TextPrediction(string Label, double Probability);

/// <summary>
/// طبقه‌بند متن «بیز ساده» تغییرناپذیر. با <see cref="Train"/> ساخته می‌شود و بعد فقط خوانده می‌شود (ایمن برای چند رشته).
/// <para>مثال: var model = NaiveBayesText.Train([("سفارشم کجاست","track"), ("سبد خرید","cart")]);
/// model.Predict("سفارش من نرسید") ← ("track"، ۰٫۹).</para>
/// </summary>
public sealed class NaiveBayesText
{
    private readonly string[] _labels;
    private readonly double[] _logPrior;
    private readonly Dictionary<string, double[]> _logLikelihood;

    private NaiveBayesText(string[] labels, double[] logPrior, Dictionary<string, double[]> logLikelihood)
    {
        _labels = labels;
        _logPrior = logPrior;
        _logLikelihood = logLikelihood;
    }

    /// <summary>برچسب‌های شناخته‌شده.</summary>
    public IReadOnlyList<string> Labels => _labels;

    /// <summary>تعداد ویژگی‌های یادگرفته‌شده.</summary>
    public int Vocabulary => _logLikelihood.Count;

    /// <summary>
    /// آموزش. نمونهٔ خالی یا بی‌برچسب نادیده گرفته می‌شود. <paramref name="alpha"/> = هموارسازی لاپلاس (پیش‌فرض ۰٫۵).
    /// <para>حداکثر ۲۰۰ هزار نمونه خوانده می‌شود تا حافظه کنترل شود.</para>
    /// </summary>
    public static NaiveBayesText Train(IEnumerable<(string Text, string Label)> samples, double alpha = 0.5)
    {
        var labelIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var docCounts = new List<int>();
        var featureCounts = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var totals = new List<long>();
        foreach (var (text, label) in samples.Take(200_000))
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(text))
                continue;
            if (!labelIndex.TryGetValue(label, out var index))
            {
                index = labelIndex.Count;
                labelIndex[label] = index;
                docCounts.Add(0);
                totals.Add(0);
            }
            docCounts[index]++;
            foreach (var feature in Features(text))
            {
                if (!featureCounts.TryGetValue(feature, out var counts))
                    featureCounts[feature] = counts = [];
                while (counts.Count <= index)
                    counts.Add(0);
                counts[index]++;
                totals[index]++;
            }
        }

        var labels = labelIndex.OrderBy(p => p.Value).Select(p => p.Key).ToArray();
        var n = labels.Length;
        var docs = docCounts.Sum();
        var vocabulary = Math.Max(1, featureCounts.Count);
        var logPrior = new double[n];
        for (var c = 0; c < n; c++)
        {
            logPrior[c] = Math.Log((docCounts[c] + 1.0) / (docs + n));
        }
        var likelihood = new Dictionary<string, double[]>(featureCounts.Count, StringComparer.Ordinal);
        foreach (var (feature, counts) in featureCounts)
        {
            var row = new double[n];
            for (var c = 0; c < n; c++)
            {
                var count = c < counts.Count ? counts[c] : 0;
                row[c] = Math.Log((count + alpha) / (totals[c] + alpha * vocabulary));
            }
            likelihood[feature] = row;
        }
        return new NaiveBayesText(labels, logPrior, likelihood);
    }

    /// <summary>
    /// سهم واژه‌های آشنای متن (۰ تا ۱). مثال: مدلی که «سفارشم» و «کجاست» را دیده، برای «سفارشم کجاست» ← ۱ و برای «زیگزاگ‌ژ سفارشم» ← ۰٫۵.
    /// برای پرهیز از حدس زدن دربارهٔ متنی که بیشترش برای مدل ناشناخته است (مثلاً نام یک کالای تازه).
    /// </summary>
    public double KnownWordRatio(string? text)
    {
        var words = PersianText.Tokenize(text ?? "");
        if (words.Count == 0)
            return 0;
        return words.Count(w => _logLikelihood.ContainsKey("w:" + w)) / (double)words.Count;
    }

    /// <summary>
    /// احتمال همهٔ برچسب‌ها (مرتب از بیشترین). متنی که هیچ ویژگی شناخته‌شده‌ای ندارد ← فهرست خالی (مدل «نمی‌داند»).
    /// </summary>
    public IReadOnlyList<TextPrediction> Distribution(string? text)
    {
        if (_labels.Length == 0 || string.IsNullOrWhiteSpace(text))
            return [];
        var scores = (double[])_logPrior.Clone();
        var known = 0;
        foreach (var feature in Features(text))
        {
            if (!_logLikelihood.TryGetValue(feature, out var row))
                continue;
            known++;
            for (var c = 0; c < scores.Length; c++)
                scores[c] += row[c];
        }
        if (known == 0)
            return [];
        var max = scores.Max();
        var exp = scores.Select(s => Math.Exp(s - max)).ToArray();
        var sum = exp.Sum();
        return exp.Select((e, c) => new TextPrediction(_labels[c], e / sum)).OrderByDescending(p => p.Probability).ToList();
    }

    /// <summary>بهترین برچسب یا null (وقتی مدل هیچ ویژگی آشنایی در متن نمی‌بیند).</summary>
    public TextPrediction? Predict(string? text)
    {
        var distribution = Distribution(text);
        return distribution.Count == 0 ? null : distribution[0];
    }

    /// <summary>
    /// ویژگی‌های یک متن: واژه‌ها («w:سفارش»)، جفت‌واژه‌ها («b:سفارش من») و سه‌حرفی‌های داخل واژه‌های ۴ حرف به بالا («c:#سف»).
    /// مثال: «سفارشم کجاست» ← w:سفارشم، w:کجاست، b:سفارشم کجاست، c:#سف، c:سفا، …
    /// </summary>
    public static IEnumerable<string> Features(string text)
    {
        var words = PersianText.Tokenize(text);
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            yield return "w:" + word;
            if (i + 1 < words.Count)
                yield return "b:" + word + " " + words[i + 1];
            if (word.Length < 4)
                continue;
            var padded = "#" + word + "#";
            for (var j = 0; j + 3 <= padded.Length; j++)
                yield return "c:" + padded.Substring(j, 3);
        }
    }
}
