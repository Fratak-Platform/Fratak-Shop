using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Learning;

/// <summary>برچسب احساس یک دیدگاه.</summary>
public enum SentimentLabel
{
    Neutral = 0,
    Positive = 1,
    Negative = 2,
}

/// <summary>
/// نتیجهٔ تحلیل احساس: <see cref="Score"/> بین −۱ (خیلی منفی) تا +۱ (خیلی مثبت)، برچسب و موضوع‌های شکایت/تعریف.
/// مثال: («کیفیتش افتضاح بود و دیر رسید»، ۱ ستاره) ← Score≈−۰٫۸، Negative، [کیفیت، ارسال].
/// </summary>
public sealed record SentimentResult(double Score, SentimentLabel Label, IReadOnlyList<string> Topics)
{
    /// <summary>منفی است؟ (برای نشان قرمز در پنل و هشدار).</summary>
    public bool IsNegative => Label == SentimentLabel.Negative;
}

/// <summary>
/// دور ۲۴ (پیشنهاد ۵۴): تحلیل احساس دیدگاه‌های فارسی، کاملاً داخلی و رایگان (بدون API).
/// <para>دو لایه:</para>
/// <list type="number">
/// <item>واژه‌نامهٔ احساس فارسی (رسمی و محاوره: «افتضاح، خراب، تقلبی، عالی، راضی‌ام …») با فهم نفی («خوب نبود» = منفی)
/// و تشدید («خیلی بد» قوی‌تر از «بد»)، ترکیب با ستاره و «پیشنهاد نمی‌کنم».</item>
/// <item>مدل یادگیرنده (بیز ساده) که از دیدگاه‌های تأییدشدهٔ خود سایت یاد می‌گیرد: ۱–۲ ستاره = منفی، ۴–۵ = مثبت
/// (<see cref="Train"/>). وقتی مدل واژه‌های متن را می‌شناسد، ۴۰٪ نظر نهایی از مدل است.</item>
/// </list>
/// <para>کاربرد: نشان «منفی» در /admin/comments و عنوان هشدار «دیدگاه منفی» برای کارکنان تا سریع‌تر پیگیری کنند
/// (هیچ دیدگاهی خودکار رد یا پنهان نمی‌شود؛ تصمیم با ادمین است).</para>
/// </summary>
public static class ReviewSentiment
{
    /// <summary>مرز منفی/مثبت.</summary>
    public const double Threshold = 0.25;

    private static readonly string[] Negative =
    [
        "بد", "افتضاح", "خراب", "خرابه", "ضعیف", "بی کیفیت", "کیفیت پایین", "کیفیتش پایین", "تقلبی", "فیک", "کپی", "شکسته", "معیوب",
        "ایراد", "ایراد داشت", "مشکل", "مشکل داشت", "پشیمان", "پشیمونم", "پشیمانم", "گرون", "گران", "دیر", "تاخیر", "نرسید", "کلاهبرداری",
        "کلاهبردار", "دروغ", "ناراضی", "متاسفانه", "متأسفانه", "کار نمیکنه", "کار نمی کنه", "کار نمی کند", "کار نکرد", "زود خراب", "سوخت",
        "پس دادم", "مرجوع کردم", "توصیه نمیکنم", "توصیه نمی کنم", "پیشنهاد نمیکنم", "پیشنهاد نمی کنم", "نخرید", "نخرین", "آشغال", "اشغال",
        "به درد نمی خوره", "بدرد نمیخوره", "به درد نمی خورد", "داغون", "بی ارزش", "اصل نبود", "اصل نیست", "شارژ نمیشه", "شارژ نمی شه",
        "قطع میشه", "قطع می شه", "پاره", "ترک", "خش", "خط و خش", "زشت", "بی کیفیته", "جواب نمیدن", "جواب نمی دن", "پاسخگو نیستن", "ناقص",
    ];

    private static readonly string[] Positive =
    [
        "عالی", "عالیه", "خوب", "خوبه", "راضی", "راضیم", "راضی ام", "ممنون", "ممنونم", "سپاس", "مرسی", "باکیفیت", "با کیفیت", "کیفیت خوب",
        "کیفیتش خوبه", "اصل", "اورجینال", "سریع", "به موقع", "توصیه میکنم", "توصیه می کنم", "پیشنهاد میکنم", "پیشنهاد می کنم", "ارزش خرید",
        "ارزان", "ارزون", "خوشحال", "بی نظیر", "فوق العاده", "محشر", "دقیق", "سالم", "تمیز", "شیک", "قشنگ", "زیبا", "عاشقش", "دوستش دارم",
        "راحت", "خوش دست", "بی نقص", "حرف نداره", "کارش خوبه", "منصفانه", "سالم رسید", "زود رسید",
    ];

    private static readonly HashSet<string> Negators = new(StringComparer.Ordinal)
    {
        "نیست", "نبود", "نیستم", "نبودم", "نیس", "نشد", "نبودن", "نیستن", "نبودند", "نیستند",
        "نداشت", "نداره", "ندارد", "نداشتم", "نداشتن", "ندیدم",
    };

    private static readonly HashSet<string> Intensifiers = new(StringComparer.Ordinal)
    {
        "خیلی", "واقعا", "واقعاً", "بسیار", "کاملا", "کاملاً", "اصلا", "اصلاً", "شدیدا", "فوق",
    };

    private static readonly (string Topic, string[] Words)[] Topics =
    [
        ("کیفیت", ["کیفیت", "جنس", "خراب", "معیوب", "شکسته", "سوخت", "ایراد", "کار نمی"]),
        ("ارسال", ["ارسال", "دیر", "تاخیر", "نرسید", "پست", "رسید", "بسته بندی", "پیک"]),
        ("اصالت", ["اصل", "تقلبی", "فیک", "کپی", "اورجینال"]),
        ("قیمت", ["قیمت", "گران", "گرون", "ارزان", "ارزون", "ارزش"]),
        ("پشتیبانی", ["پشتیبانی", "جواب", "پاسخ", "تماس", "کلاهبردار"]),
    ];

    /// <summary>
    /// تحلیل یک دیدگاه. <paramref name="rating"/> ۱ تا ۵ (اختیاری)، <paramref name="recommended"/> پاسخ «پیشنهاد می‌کنید؟»،
    /// <paramref name="model"/> مدل یادگرفته از سایت (اختیاری).
    /// مثال: Analyze("اصلا راضی نیستم، خراب رسید", 2) ← Negative.
    /// </summary>
    public static SentimentResult Analyze(string? text, byte? rating = null, bool? recommended = null, NaiveBayesText? model = null)
    {
        var tokens = PersianText.Tokenize(text);
        var lexicon = LexiconScore(tokens, out var hits);
        double score;
        if (rating is >= 1 and <= 5)
        {
            var stars = (rating.Value - 3) / 2.0;
            score = hits == 0 ? stars : 0.5 * lexicon + 0.5 * stars;
        }
        else
        {
            score = lexicon;
        }

        if (model is not null && tokens.Count > 0 && model.KnownWordRatio(string.Join(' ', tokens)) >= 0.5)
        {
            var distribution = model.Distribution(string.Join(' ', tokens));
            var positive = distribution.FirstOrDefault(p => p.Label == "pos").Probability;
            var negative = distribution.FirstOrDefault(p => p.Label == "neg").Probability;
            score = 0.6 * score + 0.4 * (positive - negative);
        }
        if (recommended == false)
            score -= 0.3;
        score = Math.Round(Math.Clamp(score, -1, 1), 3);

        var label = score <= -Threshold ? SentimentLabel.Negative : score >= Threshold ? SentimentLabel.Positive : SentimentLabel.Neutral;
        var padded = " " + string.Join(' ', tokens) + " ";
        var topics = Topics.Where(t => t.Words.Any(w => padded.Contains(w, StringComparison.Ordinal))).Select(t => t.Topic).ToList();
        return new SentimentResult(score, label, topics);
    }

    /// <summary>
    /// امتیاز واژه‌نامه بین −۱ و +۱ و تعداد واژه‌های احساسی پیداشده. عبارت‌های چندواژه‌ای اول بررسی می‌شوند
    /// تا «کار نمی کنه» یک‌بار منفی شمرده شود. واژهٔ مثبت + نفی بعدش («خوب نبود») = منفی؛ تشدید قبلش ×۱٫۵.
    /// </summary>
    public static double LexiconScore(IReadOnlyList<string> tokens, out int hits)
    {
        hits = 0;
        if (tokens.Count == 0)
            return 0;
        double positive = 0, negative = 0;
        var used = new bool[tokens.Count];
        foreach (var (phrases, isPositive) in new[] { (Negative, false), (Positive, true) })
        {
            foreach (var phrase in phrases.OrderByDescending(p => p.Count(c => c == ' ')))
            {
                var parts = phrase.Split(' ');
                for (var i = 0; i + parts.Length <= tokens.Count; i++)
                {
                    if (Enumerable.Range(0, parts.Length).Any(k => used[i + k] || tokens[i + k] != parts[k]))
                        continue;
                    for (var k = 0; k < parts.Length; k++)
                        used[i + k] = true;
                    hits++;
                    var weight = i > 0 && Intensifiers.Contains(tokens[i - 1]) ? 1.5 : 1.0;
                    var after = i + parts.Length;
                    var negated = (after < tokens.Count && Negators.Contains(tokens[after]))
                                  || (after + 1 < tokens.Count && Negators.Contains(tokens[after + 1]) && tokens[after].Length <= 3);
                    if (isPositive != negated)
                        positive += weight;
                    else
                        negative += weight;
                }
            }
        }
        return hits == 0 ? 0 : (positive - negative) / (positive + negative + 0.5);
    }

    /// <summary>
    /// آموزش مدل از دیدگاه‌های ستاره‌دار سایت: ۱–۲ ستاره «neg»، ۴–۵ «pos» (۳ ستاره کنار گذاشته می‌شود).
    /// کمتر از ۱۰ نمونه در هر طرف ← null (داده برای یادگیری کافی نیست؛ فقط واژه‌نامه کار می‌کند).
    /// </summary>
    public static NaiveBayesText? Train(IEnumerable<(string Text, byte Rating)> reviews)
    {
        var samples = reviews.Where(r => r.Rating != 3 && !string.IsNullOrWhiteSpace(r.Text))
            .Select(r => (Text: string.Join(' ', PersianText.Tokenize(r.Text)), Label: r.Rating <= 2 ? "neg" : "pos"))
            .Where(s => s.Text.Length > 0)
            .Take(20_000)
            .ToList();
        return samples.Count(s => s.Label == "neg") < 10 || samples.Count(s => s.Label == "pos") < 10 ? null : NaiveBayesText.Train(samples);
    }
}
