using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// یک نظر واقعی خریدار از یک منبع (نظرهای خود سایت یا دیجی‌کالا).
/// <c>Rating</c> بین ۱ تا ۵ (نامعلوم = null)؛ <c>Pros</c>/<c>Cons</c> نکته‌های مثبت/منفی که خود کاربر جدا نوشته؛
/// <c>Recommends</c> = «خرید این کالا را پیشنهاد می‌کنم».
/// </summary>
public sealed record SourceReview(string Source, string? Title, string? Body, int? Rating, IReadOnlyList<string> Pros, IReadOnlyList<string> Cons, bool? Recommends);

/// <summary>یک نکته با تعداد خریدارانی که آن را گفته‌اند. مثال: («شارژدهی طولانی»، ۱۲).</summary>
public sealed record InsightPoint(string Text, int Mentions);

/// <summary>یک پرسش‌وپاسخ کوتاه.</summary>
public sealed record InsightFaq(string Question, string Answer);

/// <summary>
/// پیش‌نویس «خلاصهٔ خرید» محصول (B10–B12): جمع‌بندی نظرها + نکته‌های مشخصات.
/// همهٔ متن‌ها با الگوهای خود سایت نوشته می‌شوند؛ از متن نظرها فقط عبارت‌های حداکثر ۵ کلمه‌ای برداشته می‌شود.
/// </summary>
public sealed class InsightDraft
{
    public string Summary { get; set; } = string.Empty;
    public List<string> ForWhom { get; set; } = [];
    public List<string> NotFor { get; set; } = [];
    public List<InsightPoint> Pros { get; set; } = [];
    public List<InsightPoint> Cons { get; set; } = [];
    public string Verdict { get; set; } = string.Empty;
    public List<InsightFaq> Faq { get; set; } = [];
    public List<string> Highlights { get; set; } = [];
    public string ShortDescription { get; set; } = string.Empty;
    public int ReviewCount { get; set; }
    public int? RatingTenths { get; set; }
    public int? RecommendPercent { get; set; }
    public List<string> Sources { get; set; } = [];

    /// <summary>چیزی برای نمایش دارد؟ (بدون نظر و بدون مشخصهٔ مهم ← هیچ چیز نشان داده نمی‌شود؛ بهتر از متن توخالی.)</summary>
    public bool HasContent => Pros.Count + Cons.Count > 0 || Highlights.Count > 0;
}

/// <summary>ورودی سنتز: محصول و مشخصاتش.</summary>
public sealed record InsightSubject(Guid Id, string Name, string? BrandName, string? CategoryName, IReadOnlyList<SeoFeature> Features);

/// <summary>
/// تحلیل نظرها و ساخت خلاصهٔ خرید (B10–B13) – کاملاً داخلی، بدون هوش مصنوعی پولی.
/// <para>روش:</para>
/// <list type="number">
/// <item>نکته‌های مثبت/منفی صریح کاربران + جمله‌هایی از متن نظر که دربارهٔ یک «جنبه» (باتری، صدا، ساخت، …) و همراه کلمهٔ احساسی‌اند.</item>
/// <item>جمله‌های مربوط به فروشنده/ارسال/بسته‌بندی حذف می‌شوند (ویژگی کالا نیستند).</item>
/// <item>عبارت‌ها با شباهت ژاکارد خوشه‌بندی و شمرده می‌شوند؛ فقط نکته‌هایی که دست‌کم ۲ نفر گفته‌اند می‌مانند (جلوگیری از دادهٔ بی‌ارزش).</item>
/// <item>خلاصه، «مناسب چه کسی/نه برای چه کسی» و جمع‌بندی با الگوهای متنوع نوشته و با محافظ کپی ۶‌کلمه‌ای بررسی می‌شود.</item>
/// </list>
/// <para>محل استفاده: SmartOrchestrator (کار «خلاصهٔ خرید»)؛ نتیجه در جدول ProductInsights و بخش «خلاصهٔ خرید» صفحهٔ محصول.</para>
/// </summary>
public static class ReviewSynthesis
{
    /// <summary>یک «جنبه» قابل بحث در نظرها، کلمه‌های نشانه، و عبارت آمادهٔ مثبت/منفی/مخاطب.</summary>
    public sealed record Aspect(string Name, string[] Cues, string Positive, string Negative, string? Audience, string? AvoidIf);

    public static readonly Aspect[] Aspects =
    [
        new("ساخت", ["ساخت", "بدنه", "جنس", "محکم", "متریال", "دوام", "مقاوم"], "ساخت محکم و باکیفیت", "کیفیت ساخت معمولی", "کسانی که دنبال کالای بادوام‌اند", "اگر استفادهٔ خشن و پرضربه دارید"),
        new("باتری", ["باتری", "شارژ", "شارژدهی", "باطری"], "شارژدهی خوب", "شارژدهی کمتر از انتظار", "کسانی که شارژدهی طولانی می‌خواهند", "اگر شارژدهی خیلی طولانی برایتان حیاتی است"),
        new("صدا", ["صدا", "بلندگو", "اسپیکر", "بیس"], "کیفیت صدای خوب", "کیفیت صدای معمولی", "کسانی که کیفیت صدا برایشان مهم است", "اگر به صدای حرفه‌ای نیاز دارید"),
        new("نمایشگر", ["صفحه", "نمایشگر", "تصویر", "وضوح"], "نمایشگر باکیفیت", "نمایشگر معمولی", "کسانی که زیاد فیلم می‌بینند", "اگر کیفیت تصویر اولویت اول شماست"),
        new("دوربین", ["دوربین", "عکاسی", "فیلمبرداری"], "دوربین خوب", "دوربین ضعیف", "علاقه‌مندان به عکاسی روزمره", "اگر عکاسی برایتان مهم است"),
        new("سرعت", ["سرعت", "عملکرد", "سریع", "کند", "هنگ", "لگ", "روان"], "عملکرد سریع و روان", "کندی در کار سنگین", "کسانی که کار روزمرهٔ سریع می‌خواهند", "اگر کارهای سنگین انجام می‌دهید"),
        new("قیمت", ["قیمت", "ارزش", "گران", "ارزان", "اقتصادی"], "ارزش خرید بالا", "قیمت نسبتاً بالا", "خریدارانی که به‌صرفه‌بودن برایشان مهم است", "اگر بودجهٔ محدودی دارید"),
        new("وزن", ["سبک", "سنگین", "وزن", "جمع‌وجور", "حجم", "اندازه", "کوچک"], "سبک و جمع‌وجور", "وزن یا حجم زیاد", "کسانی که زیاد جابه‌جا می‌کنند", "اگر وسیلهٔ خیلی سبک می‌خواهید"),
        new("راحتی", ["راحت", "کاربری", "ارگونومی", "ارگونومیک"], "استفادهٔ راحت", "راحتی استفادهٔ کم", "کسانی که ساعت‌های طولانی استفاده می‌کنند", "اگر استفادهٔ طولانی‌مدت دارید"),
        new("ظاهر", ["ظاهر", "طراحی", "شیک", "زیبا", "خوشگل", "قشنگ"], "ظاهر شیک", "طراحی معمولی", null, null),
        new("گرما", ["داغ", "حرارت", "گرما"], "گرم‌نشدن در استفاده", "داغ‌شدن هنگام کار", null, "اگر استفادهٔ طولانی و پیوسته دارید"),
        new("اتصال", ["بلوتوث", "اتصال", "وای‌فای", "وایفای", "قطع", "آنتن"], "اتصال پایدار", "قطع‌ووصل شدن اتصال", null, "اگر اتصال کاملاً پایدار برایتان حیاتی است"),
    ];

    public static readonly string[] PositiveWords =
    [
        "عالی", "عالیه", "خوب", "خوبه", "راضی", "راضیم", "قوی", "قویه", "محکم", "سریع", "شیک", "زیبا", "ارزان", "سبک", "راحت", "ارزش", "بی‌نظیر",
        "کافی", "تمیز", "باکیفیت", "مناسب", "روان", "بالا", "طولانی", "پایدار", "فوق‌العاده", "خوش", "بینظیر", "ممتاز",
    ];

    public static readonly string[] NegativeWords =
    [
        "ضعیف", "ضعیفه", "بد", "بده", "کم", "خراب", "کند", "داغ", "سنگین", "گران", "گرون", "ناراضی", "افتضاح", "شکست", "ایراد", "مشکل", "قطع",
        "هنگ", "لگ", "نامناسب", "پایین", "ضعف", "بی‌کیفیت", "پشیمان", "نمیارزه", "نمی‌ارزه", "کوتاه",
    ];

    private static readonly string[] Negations = ["نیست", "نبود", "نداره", "ندارد", "نه", "نمی", "نمیشه", "نشد", "نیستش"];

    /// <summary>جمله‌هایی که دربارهٔ فروشنده/ارسال‌اند و ویژگی کالا نیستند.</summary>
    public static readonly string[] OffTopic =
    [
        "ارسال", "پیک", "فروشنده", "بسته‌بندی", "بسته بندی", "دیجی", "ترب", "باسلام", "تحویل", "پست", "سفارش", "مرجوع", "پشتیبانی", "تخفیف", "شگفت", "کارتن",
    ];

    /// <summary>
    /// نکته‌های تکرارشده را استخراج می‌کند. خروجی: (مثبت‌ها، منفی‌ها) به ترتیب تکرار، هر کدام حداکثر <paramref name="max"/>.
    /// مثال: ۳ نفر «باتری قوی» و ۲ نفر «باتریش عالیه» ← («شارژدهی خوب»، ۵).
    /// </summary>
    public static (List<InsightPoint> Pros, List<InsightPoint> Cons) ExtractPoints(IReadOnlyList<SourceReview> reviews, int minMentions = 2, int max = 5)
    {
        var positive = new List<string>();
        var negative = new List<string>();
        var aspectPositive = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var aspectNegative = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        for (var i = 0; i < reviews.Count; i++)
        {
            var review = reviews[i];
            foreach (var pro in review.Pros)
                AddExplicit(pro, positive, aspectPositive, i);
            foreach (var con in review.Cons)
                AddExplicit(con, negative, aspectNegative, i);

            foreach (var sentence in PersianText.Sentences(review.Title + ". " + review.Body))
            {
                if (IsOffTopic(sentence))
                    continue;
                var aspect = FindAspect(sentence);
                var polarity = Polarity(sentence);
                if (aspect is null || polarity == 0)
                    continue;
                (polarity > 0 ? aspectPositive : aspectNegative).GetOrAdd(aspect.Name).Add(i);
            }
        }

        // جنبه‌ها (هر خریدار یک رأی) + عبارت‌های صریحی که به هیچ جنبه‌ای نخوردند (خوشه‌بندی‌شده).
        var pros = Combine(aspectPositive, positive, a => a.Positive, minMentions);
        var cons = Combine(aspectNegative, negative, a => a.Negative, minMentions);

        // یک جنبه نمی‌تواند هم «نقطهٔ قوت» و هم «ضعف» باشد؛ طرفی که رأی بیشتری دارد می‌ماند (تساوی ← هیچ‌کدام).
        foreach (var aspect in Aspects)
        {
            var p = pros.FirstOrDefault(x => x.Text == aspect.Positive);
            var c = cons.FirstOrDefault(x => x.Text == aspect.Negative);
            if (p is null || c is null)
                continue;
            if (p.Mentions > c.Mentions * 2) cons.Remove(c);
            else if (c.Mentions > p.Mentions * 2) pros.Remove(p);
            else { pros.Remove(p); cons.Remove(c); }
        }

        return (pros.OrderByDescending(p => p.Mentions).Take(max).ToList(), cons.OrderByDescending(c => c.Mentions).Take(max).ToList());

        static void AddExplicit(string raw, List<string> bucket, Dictionary<string, HashSet<int>> aspects, int reviewIndex)
        {
            var phrase = SmartText.ShortPhrase(raw);
            if (phrase.Length < 3 || IsOffTopic(phrase))
                return;
            var aspect = FindAspect(phrase);
            if (aspect is not null)
                aspects.GetOrAdd(aspect.Name).Add(reviewIndex);
            else
                bucket.Add(phrase);
        }
    }

    private static List<InsightPoint> Combine(Dictionary<string, HashSet<int>> aspects, List<string> loose, Func<Aspect, string> label, int minMentions)
    {
        var list = new List<InsightPoint>();
        foreach (var aspect in Aspects)
        {
            if (aspects.TryGetValue(aspect.Name, out var voters) && voters.Count >= minMentions)
                list.Add(new InsightPoint(label(aspect), voters.Count));
        }
        foreach (var cluster in SmartText.Cluster(loose))
        {
            if (cluster.Count >= minMentions && cluster.Label.Length is >= 3 and <= 60)
                list.Add(new InsightPoint(cluster.Label, cluster.Count));
        }
        return list;
    }

    /// <summary>جمله دربارهٔ فروشنده/ارسال است؟</summary>
    public static bool IsOffTopic(string text) => OffTopic.Any(word => text.Contains(word, StringComparison.Ordinal));

    /// <summary>اولین جنبه‌ای که کلمهٔ نشانه‌اش در متن آمده (یا null).</summary>
    public static Aspect? FindAspect(string text)
    {
        var tokens = PersianText.Tokenize(text);
        foreach (var aspect in Aspects)
        {
            if (aspect.Cues.Any(cue => tokens.Any(t => MatchesCue(t, PersianText.Normalize(cue)))))
                return aspect;
        }
        return null;
    }

    /// <summary>
    /// کلمه همان نشانه است یا نشانه + پسوند کوتاه محاوره‌ای/جمع. مثال: «باتریش»، «صدای»، «صداها» ✓؛ «گوشی» برای نشانهٔ «گوش» ✗ (نشانهٔ «گوش» وجود ندارد).
    /// </summary>
    private static bool MatchesCue(string token, string cue) =>
        token == cue || token.StartsWith(cue, StringComparison.Ordinal) && token[cue.Length..] is "ش" or "ه" or "ی" or "ها" or "های" or "یش" or "اش" or "شو" or "هاش";

    /// <summary>
    /// قطبیت ساده: +۱ مثبت، -۱ منفی، ۰ نامعلوم/مخلوط. نفی («نیست، نداره») قطبیت را برعکس می‌کند.
    /// مثال: «باتریش قوی نیست» ← -۱؛ «اصلاً داغ نمی‌کنه» ← +۱.
    /// </summary>
    public static int Polarity(string sentence)
    {
        var tokens = PersianText.Tokenize(sentence);
        var pos = tokens.Count(t => PositiveWords.Contains(t));
        var neg = tokens.Count(t => NegativeWords.Contains(t));
        if (pos == neg)
            return 0;
        var polarity = pos > neg ? 1 : -1;
        if (tokens.Any(t => Negations.Contains(t) || t.StartsWith("نمی", StringComparison.Ordinal)))
            polarity = -polarity;
        return polarity;
    }

    /// <summary>
    /// ساخت کامل پیش‌نویس خلاصهٔ خرید. خروجی همیشه معتبر است؛ اگر داده کم باشد بخش‌ها خالی می‌مانند (HasContent = false).
    /// </summary>
    public static InsightDraft Build(InsightSubject subject, IReadOnlyList<SourceReview> reviews)
    {
        var seed = subject.Id.ToString("N");
        var draft = new InsightDraft
        {
            ReviewCount = reviews.Count,
            Sources = reviews.Select(r => r.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };

        var ratings = reviews.Where(r => r.Rating is >= 1 and <= 5).Select(r => r.Rating!.Value).ToList();
        if (ratings.Count >= 3)
            draft.RatingTenths = (int)Math.Round(ratings.Average() * 10);
        var votes = reviews.Where(r => r.Recommends.HasValue).ToList();
        if (votes.Count >= 5)
            draft.RecommendPercent = (int)Math.Round(100.0 * votes.Count(v => v.Recommends == true) / votes.Count);

        var minMentions = reviews.Count >= 40 ? 3 : 2;
        (draft.Pros, draft.Cons) = ExtractPoints(reviews, minMentions);

        var facts = subject.Features
            .Select(SpecKnowledge.Read).Where(f => f is not null).Select(f => f!)
            .OrderByDescending(f => SpecKnowledge.Weight(f.Topic)).ToList();
        draft.Highlights = facts.Select(SpecKnowledge.Highlight).Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h!)
            .Distinct(StringComparer.Ordinal).Take(5).ToList();

        // مناسب چه کسانی: اول از دادهٔ فنی، بعد از نقطه‌قوت‌هایی که خریداران گفته‌اند.
        var forWhom = facts.Select(SpecKnowledge.Audience).Where(a => a is not null).Select(a => a!).ToList();
        foreach (var pro in draft.Pros)
        {
            var aspect = Aspects.FirstOrDefault(a => a.Positive == pro.Text);
            if (aspect?.Audience is { } audience)
                forWhom.Add(audience);
        }
        draft.ForWhom = forWhom.Distinct(StringComparer.Ordinal).Take(4).ToList();
        draft.NotFor = draft.Cons
            .Select(c => Aspects.FirstOrDefault(a => a.Negative == c.Text)?.AvoidIf)
            .Where(a => a is not null).Select(a => a! + "، گزینه‌های دیگر را هم ببینید").Distinct(StringComparer.Ordinal).Take(3).ToList();

        draft.Summary = BuildSummary(subject, draft, seed);
        draft.Verdict = BuildVerdict(subject, draft, seed);
        draft.Faq = BuildFaq(subject, facts, seed);
        draft.ShortDescription = BuildShortDescription(subject, draft, seed);

        // محافظ کپی: هیچ متن بلند تولیدی نباید ۶ کلمهٔ پشت‌سرهم از نظرها داشته باشد.
        var sources = reviews.Select(r => r.Title + " " + r.Body + " " + string.Join(' ', r.Pros) + " " + string.Join(' ', r.Cons)).ToList();
        if (SmartText.IsCopied(draft.Summary, sources)) draft.Summary = string.Empty;
        if (SmartText.IsCopied(draft.Verdict, sources)) draft.Verdict = string.Empty;
        if (SmartText.IsCopied(draft.ShortDescription, sources)) draft.ShortDescription = string.Empty;
        return draft;
    }

    private static string ShortName(InsightSubject subject)
    {
        var name = PersianText.Clean(subject.Name);
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 6 ? string.Join(' ', words.Take(6)) : name;
    }

    private static string BuildSummary(InsightSubject subject, InsightDraft d, string seed)
    {
        if (d.Pros.Count == 0 && d.Cons.Count == 0)
            return string.Empty;
        var parts = new List<string>();
        var count = PersianText.Digits(d.ReviewCount);
        if (d.Pros.Count > 0)
        {
            var top = PersianText.JoinList(d.Pros.Take(2).Select(p => p.Text).ToList());
            parts.Add(SmartText.Pick(seed,
                $"در {count} نظر بررسی‌شده، خریداران بیش از همه به {top} اشاره کرده‌اند",
                $"بیشترین رضایت خریداران (از میان {count} نظر) از {top} است",
                $"جمع‌بندی {count} نظر خریداران: {top} پرتکرارترین نقطهٔ قوت است"));
        }
        if (d.Cons.Count > 0)
        {
            parts.Add(SmartText.Pick(seed + "c",
                $"رایج‌ترین ایرادی که گفته شده «{d.Cons[0].Text}» است",
                $"مهم‌ترین نکتهٔ منفی از نگاه خریداران «{d.Cons[0].Text}» است",
                $"در مقابل، چند نفر به «{d.Cons[0].Text}» اشاره کرده‌اند"));
        }
        else if (d.ReviewCount >= 5)
        {
            parts.Add("ایراد تکرارشونده‌ای در نظرها دیده نشد");
        }
        var text = string.Join("؛ ", parts) + ".";
        if (d.RecommendPercent is { } percent && d.ReviewCount >= 5)
            text += $" {PersianText.Digits(percent)}٪ از خریدارانی که نظر داده‌اند خرید آن را پیشنهاد کرده‌اند.";
        return text;
    }

    private static string BuildVerdict(InsightSubject subject, InsightDraft d, string seed)
    {
        var name = ShortName(subject);
        if (d.Pros.Count == 0 && d.Highlights.Count == 0)
            return string.Empty;
        var strength = d.Pros.Count > 0 ? d.Pros[0].Text : d.Highlights[0];
        var rating = d.RatingTenths ?? 0;
        var opening = rating >= 42
            ? SmartText.Pick(seed + "v", $"{name} انتخاب مطمئنی است", $"{name} از نگاه خریدارانش خرید موفقی بوده", $"{name} رضایت بالایی داشته")
            : rating is > 0 and < 32
                ? SmartText.Pick(seed + "v", $"{name} نظرهای متفاوتی گرفته", $"دربارهٔ {name} نظرها یکدست نیست")
                : SmartText.Pick(seed + "v", $"{name} گزینهٔ قابل‌بررسی است", $"{name} برای بیشتر کاربردها کار را راه می‌اندازد");
        var text = $"{opening}، به‌ویژه اگر «{strength}» برایتان مهم است.";
        if (d.Cons.Count > 0)
            text += $" قبل از خرید «{d.Cons[0].Text}» را در نظر بگیرید.";
        return text;
    }

    private static List<InsightFaq> BuildFaq(InsightSubject subject, IReadOnlyList<SpecFact> facts, string seed)
    {
        var name = ShortName(subject);
        var list = new List<InsightFaq>();
        foreach (var fact in facts)
        {
            if (SpecKnowledge.Faq(fact) is not { } faq || faq.Questions.Length == 0 || faq.Answers.Length == 0)
                continue;
            var question = Expand(SmartText.Pick(seed + fact.Key, faq.Questions), seed).Replace("{S}", name, StringComparison.Ordinal);
            var answer = Expand(SmartText.Pick(seed + fact.Key + "a", faq.Answers), seed).Replace("{S}", name, StringComparison.Ordinal);
            if (list.All(f => f.Question != question))
                list.Add(new InsightFaq(question, answer));
            if (list.Count == 4)
                break;
        }
        return list;
    }

    private static string Expand(string pattern, string seed)
    {
        var options = Spin.Expand(pattern, 16);
        return options.Count == 0 ? pattern : SmartText.Pick(seed + pattern, options.ToArray());
    }

    private static string BuildShortDescription(InsightSubject subject, InsightDraft d, string seed)
    {
        var name = ShortName(subject);
        var parts = new List<string>();
        if (d.Highlights.Count > 0)
            parts.Add($"{name} با {PersianText.JoinList(d.Highlights.Take(3).ToList())}");
        if (d.Pros.Count > 0)
        {
            parts.Add(SmartText.Pick(seed + "s",
                $"خریداران از {d.Pros[0].Text} آن راضی‌اند",
                $"نقطهٔ قوت اصلی آن از نگاه خریداران {d.Pros[0].Text} است"));
        }
        if (parts.Count == 0)
            return string.Empty;
        var text = string.Join("؛ ", parts) + ".";
        return PersianText.Truncate(text, 300);
    }

    private static HashSet<int> GetOrAdd(this Dictionary<string, HashSet<int>> map, string key)
    {
        if (!map.TryGetValue(key, out var set))
            map[key] = set = [];
        return set;
    }
}
