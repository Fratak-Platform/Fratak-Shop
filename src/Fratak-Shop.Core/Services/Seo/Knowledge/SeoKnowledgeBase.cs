using System.Collections.Frozen;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Knowledge;

/// <summary>
/// یک ردیف دانش آمادهٔ استفاده (نسخهٔ فقط‌خواندنی ردیف دیتابیس یا دادهٔ اولیه).
/// <para><see cref="Confidence"/> = وزن × ضریب یادگیری: هر پذیرش کمی بالا و هر رد بیشتر پایینش می‌آورد.
/// مثال: وزن ۱ بدون بازخورد ← ۱؛ بعد از ۲ رد ← ۰٫۵۷ (دیگر خودکار اعمال نمی‌شود و فقط پیشنهاد می‌شود).</para>
/// </summary>
public sealed record KnowledgeRule(SeoKnowledgeKind Kind, string Term, string? Value, string Topic, double Weight, int Successes = 0, int Rejections = 0)
{
    /// <summary>حد اعمال خودکار: اصلاح با اطمینان کمتر فقط به‌صورت «یادداشت» نشان داده می‌شود.</summary>
    public const double AutoApply = 0.7;

    public double Confidence => Weight * (Successes + 4.0) / (Successes + 4.0 + Rejections * 1.5);

    public bool IsAuto => Confidence >= AutoApply;

    /// <summary>شناسهٔ کوتاه برای بازخورد. مثال: «7:باطری» (نوع ۷ = Correction).</summary>
    public string Id => (int)Kind + ":" + Term;
}

/// <summary>ردیف خام برای ساخت <see cref="SeoKnowledgeBase"/> (از دیتابیس یا فایل).</summary>
public sealed record KnowledgeRow(SeoKnowledgeKind Kind, string Term, string? Value, string Topic, double Weight,
    int Successes = 0, int Rejections = 0, SeoKnowledgeSource Source = SeoKnowledgeSource.Seed);

/// <summary>
/// «حافظهٔ» موتور سئو و نگارش: نمای فقط‌خواندنی و سریع جدول دانش (مرحلهٔ شانزدهم). امن برای استفادهٔ هم‌زمان.
/// <para>ساخت: <see cref="SeoKnowledgeService"/> هر چند دقیقه از دیتابیس (یا اگر هنوز بذر نخورده، از فایل جاسازی‌شده).
/// استفاده: <c>PersianWriting</c> و <c>TextRefiner</c> (املا، نیم‌فاصله، سبک)، <see cref="SpamGuard"/> (جلوگیری از جریمه)،
/// <c>KeywordPlanner</c> و سئوی درجا (عبارت‌های آموخته و نیت جست‌وجو).</para>
/// </summary>
public sealed class SeoKnowledgeBase
{
    /// <summary>دانش خالی (وقتی هیچ داده‌ای در دسترس نیست؛ همه چیز بدون خطا کار می‌کند ولی بدون کمک دانش).</summary>
    public static readonly SeoKnowledgeBase Empty = new([], includeSeedLexicon: false);

    private static readonly Lazy<FrozenSet<string>> SeedLexicon = new(() =>
        SeoKnowledgeSeed.Rows.Where(r => r.Kind == SeoKnowledgeKind.Word).Select(r => PersianText.Normalize(r.Term)).ToFrozenSet(StringComparer.Ordinal),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly bool _includeSeedLexicon;
    private readonly Dictionary<string, double> _frequency = new(StringComparer.Ordinal);
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stop = new(StringComparer.Ordinal);
    private readonly HashSet<string> _presentStems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pastStems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<KnowledgeRule>> _phrases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KnowledgeRule> _keywords = new(StringComparer.Ordinal);

    public SeoKnowledgeBase(IEnumerable<KnowledgeRow> rows, bool includeSeedLexicon = true)
    {
        _includeSeedLexicon = includeSeedLexicon;
        var corrections = new List<KnowledgeRule>();
        var styles = new List<KnowledgeRule>();
        var informals = new List<KnowledgeRule>();
        var units = new List<KnowledgeRule>();
        var compounds = new Dictionary<string, KnowledgeRule>(StringComparer.Ordinal);
        var numbers = new Dictionary<string, string>(StringComparer.Ordinal);
        var modifiers = new List<KnowledgeRule>();
        var power = new List<KnowledgeRule>();
        var spam = new List<KnowledgeRule>();
        var transitions = new List<KnowledgeRule>();
        var ctas = new List<KnowledgeRule>();
        var benefits = new List<KnowledgeRule>();
        var headings = new List<KnowledgeRule>();
        var questions = new List<KnowledgeRule>();
        var patterns = new List<KnowledgeRule>();
        var synonyms = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var counts = new Dictionary<SeoKnowledgeKind, int>();
        var sources = new Dictionary<SeoKnowledgeSource, int>();

        foreach (var row in rows)
        {
            counts[row.Kind] = counts.GetValueOrDefault(row.Kind) + 1;
            sources[row.Source] = sources.GetValueOrDefault(row.Source) + 1;
            var rule = new KnowledgeRule(row.Kind, row.Term, row.Value, row.Topic ?? "", row.Weight, row.Successes, row.Rejections);
            switch (row.Kind)
            {
                case SeoKnowledgeKind.Word:
                    var word = PersianText.Normalize(row.Term);
                    _known.Add(word);
                    if (row.Weight > 0)
                        _frequency[word] = Math.Max(_frequency.GetValueOrDefault(word), row.Weight);
                    break;
                case SeoKnowledgeKind.Compound:
                    if (!string.IsNullOrEmpty(row.Value))
                    {
                        compounds[row.Term] = rule;
                        foreach (var part in row.Value.Split(PersianText.Zwnj, ' '))
                            _known.Add(PersianText.Normalize(part));
                        _known.Add(PersianText.Normalize(row.Value).Replace(" ", ""));
                    }
                    break;
                case SeoKnowledgeKind.Verb:
                    _pastStems.Add(PersianText.Normalize(row.Term));
                    if (!string.IsNullOrEmpty(row.Value))
                        _presentStems.Add(PersianText.Normalize(row.Value));
                    break;
                case SeoKnowledgeKind.StopWord:
                    _stop.Add(PersianText.Normalize(row.Term));
                    break;
                case SeoKnowledgeKind.Informal: informals.Add(rule); break;
                case SeoKnowledgeKind.Correction: corrections.Add(rule); break;
                case SeoKnowledgeKind.Style: styles.Add(rule); break;
                case SeoKnowledgeKind.Unit: units.Add(rule); break;
                case SeoKnowledgeKind.Number:
                    if (!string.IsNullOrEmpty(row.Value)) numbers[row.Term] = row.Value;
                    break;
                case SeoKnowledgeKind.Modifier: modifiers.Add(rule); break;
                case SeoKnowledgeKind.PowerWord: power.Add(rule); break;
                case SeoKnowledgeKind.Spam: spam.Add(rule); break;
                case SeoKnowledgeKind.Transition: transitions.Add(rule); break;
                case SeoKnowledgeKind.CallToAction: ctas.Add(rule); break;
                case SeoKnowledgeKind.Benefit: benefits.Add(rule); break;
                case SeoKnowledgeKind.Heading: headings.Add(rule); break;
                case SeoKnowledgeKind.Question: questions.Add(rule); break;
                case SeoKnowledgeKind.Pattern: patterns.Add(rule); break;
                case SeoKnowledgeKind.Synonym:
                    if (!string.IsNullOrEmpty(row.Value) && rule.Confidence >= 0.5)
                        synonyms[row.Term] = row.Value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(v => v != row.Term).ToArray();
                    break;
                case SeoKnowledgeKind.Phrase:
                    var topicKey = PersianText.Normalize(row.Topic);
                    if (!_phrases.TryGetValue(topicKey, out var list))
                        _phrases[topicKey] = list = [];
                    list.Add(rule);
                    break;
                case SeoKnowledgeKind.Keyword:
                    _keywords[PersianText.Normalize(row.Term)] = rule;
                    break;
            }
        }

        // عبارت‌های طولانی‌تر اول (تا «لازم به ذکر است که» پیش از «لازم به ذکر است» اعمال شود).
        static IReadOnlyList<KnowledgeRule> Longest(List<KnowledgeRule> list) => list.OrderByDescending(r => r.Term.Length).ThenBy(r => r.Term, StringComparer.Ordinal).ToList();
        Corrections = Longest(corrections);
        Styles = Longest(styles);
        Informals = Longest(informals);
        Units = Longest(units);
        Spam = Longest(spam);
        Compounds = compounds;
        Numbers = numbers;
        Modifiers = modifiers.OrderByDescending(r => r.Confidence).ToList();
        PowerWords = power.OrderByDescending(r => r.Confidence).ToList();
        Transitions = transitions.OrderByDescending(r => r.Confidence).ToList();
        CallsToAction = ctas.OrderByDescending(r => r.Confidence).ToList();
        Benefits = benefits.OrderByDescending(r => r.Confidence).ToList();
        Headings = headings.OrderByDescending(r => r.Confidence).ToList();
        Questions = questions.OrderByDescending(r => r.Confidence).ToList();
        Patterns = patterns.OrderByDescending(r => r.Confidence).ToList();
        Synonyms = synonyms;
        Counts = counts;
        Sources = sources;
        Total = counts.Values.Sum();
        foreach (var stop in PersianText.StopWords)
            _stop.Add(stop);
    }

    /// <summary>دانش فقط از فایل جاسازی‌شده (بدون دیتابیس). برای آزمون‌ها و پیش از اولین بذر.</summary>
    public static SeoKnowledgeBase FromSeed() =>
        new(SeoKnowledgeSeed.StoredRows.Select(r => new KnowledgeRow(r.Kind, r.Term, r.Value, r.Topic, r.Weight)));

    public int Total { get; }
    public IReadOnlyDictionary<SeoKnowledgeKind, int> Counts { get; }
    public IReadOnlyDictionary<SeoKnowledgeSource, int> Sources { get; }

    public IReadOnlyList<KnowledgeRule> Corrections { get; }
    public IReadOnlyList<KnowledgeRule> Styles { get; }
    public IReadOnlyList<KnowledgeRule> Informals { get; }
    public IReadOnlyList<KnowledgeRule> Units { get; }
    public IReadOnlyList<KnowledgeRule> Spam { get; }
    /// <summary>شکل جدا/سرهم ← قاعدهٔ واژهٔ مرکب. مثال: «نرم افزار» ← «نرم‌افزار».</summary>
    public IReadOnlyDictionary<string, KnowledgeRule> Compounds { get; }
    /// <summary>عدد به حروف ← رقم فارسی. مثال: «بیست و پنج» ← «۲۵».</summary>
    public IReadOnlyDictionary<string, string> Numbers { get; }
    public IReadOnlyList<KnowledgeRule> Modifiers { get; }
    public IReadOnlyList<KnowledgeRule> PowerWords { get; }
    public IReadOnlyList<KnowledgeRule> Transitions { get; }
    public IReadOnlyList<KnowledgeRule> CallsToAction { get; }
    public IReadOnlyList<KnowledgeRule> Benefits { get; }
    public IReadOnlyList<KnowledgeRule> Headings { get; }
    public IReadOnlyList<KnowledgeRule> Questions { get; }
    public IReadOnlyList<KnowledgeRule> Patterns { get; }
    /// <summary>واژه ← هم‌معنی‌های امن. مثال: «متنوع» ← [«گوناگون»].</summary>
    public IReadOnlyDictionary<string, string[]> Synonyms { get; }

    // ── واژه‌نامه ────────────────────────────────────────────────────────

    /// <summary>واژهٔ ایست است؟ (حرف اضافه، ربط …)</summary>
    public bool IsStopWord(string token) => _stop.Contains(PersianText.Normalize(token));

    /// <summary>بسامد لگاریتمی واژه در پیکرهٔ فارسی (۰ = ناشناخته یا نادر). مثال: «کیفیت» ≈ ۶٫۸.</summary>
    public double Frequency(string word) => _frequency.GetValueOrDefault(PersianText.Normalize(word));

    /// <summary>
    /// واژهٔ فارسی شناخته‌شده است؟ (خود واژه، یا بعد از جدا کردن پسوندهای جمع/مالکیت/صفت برتر، یا یک فعل صرف‌شده).
    /// مثال: «کتاب‌هایمان»، «می‌خریدیم»، «بزرگ‌ترین» → true؛ «باطری» → true (در واژه‌نامه هست)؛ «سلاممم» → false.
    /// </summary>
    public bool IsKnownWord(string word)
    {
        var normalized = PersianText.Normalize(word);
        if (normalized.Length == 0)
            return true;
        if (normalized.Contains(' '))
            return normalized.Split(' ').All(IsKnownWord);
        if (!normalized.All(PersianText.IsPersianLetter))
            return true; // لاتین، عدد و نماد را املایابی فارسی نمی‌سنجد.
        return Known(normalized) || Stripped(normalized).Any(Known) || IsVerbForm(normalized);
    }

    /// <summary>
    /// خود این واژه (بدون جدا کردن پسوند و بدون صرف فعل) در واژه‌نامه هست؟ مثال: «میز» → true، «میخواهم» → false.
    /// </summary>
    public bool IsInLexicon(string word) => Known(PersianText.Normalize(word));

    private bool Known(string normalized) =>
        _known.Contains(normalized) || _stop.Contains(normalized) || _includeSeedLexicon && SeedLexicon.Value.Contains(normalized);

    private static readonly string[] Suffixes =
    [
        "هایمان", "هایتان", "هایشان", "هایی", "هایم", "هایت", "هایش", "های", "ها",
        "ترین", "تر", "مان", "تان", "شان", "یان", "گان", "ان", "ات", "ام", "اش", "ای", "یم", "ید", "ند", "ی", "م", "ت", "ش",
    ];

    private static IEnumerable<string> Stripped(string word)
    {
        foreach (var suffix in Suffixes)
        {
            if (word.Length - suffix.Length < 2 || !word.EndsWith(suffix, StringComparison.Ordinal))
                continue;
            var stem = word[..^suffix.Length];
            yield return stem;
            // «خانه‌ای» ← «خانه»، «زیبایی» ← «زیبا»
            if (stem.EndsWith('ی') && stem.Length > 2)
                yield return stem[..^1];
            foreach (var inner in new[] { "ها", "تر", "ی" })
                if (stem.Length - inner.Length >= 2 && stem.EndsWith(inner, StringComparison.Ordinal))
                    yield return stem[..^inner.Length];
        }
    }

    private static readonly string[] VerbPrefixes = ["نمی", "می", "ن", "ب", ""];
    private static readonly string[] VerbEndings = ["یم", "ید", "ند", "ه", "م", "ی", "د", "ن", "ده", "ست", "ستن", "دن", "تن", "ان", ""];

    /// <summary>فعل صرف‌شده؟ مثال: «نمی‌خرم» (نمی + خر + م)، «خریدند» (خرید + ند)، «بخرید».</summary>
    public bool IsVerbForm(string normalized)
    {
        if (_presentStems.Count == 0 && _pastStems.Count == 0)
            return false;
        normalized = normalized.Replace(" ", "");
        foreach (var prefix in VerbPrefixes)
        {
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var rest = normalized[prefix.Length..];
            foreach (var ending in VerbEndings)
            {
                if (rest.Length - ending.Length < 1 || !rest.EndsWith(ending, StringComparison.Ordinal))
                    continue;
                var stem = rest[..^ending.Length];
                if (_pastStems.Contains(stem) || _presentStems.Contains(stem) || stem.EndsWith('ی') && _presentStems.Contains(stem[..^1]))
                    return true;
            }
        }
        return false;
    }

    private const string PersianLetters = "ابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهیآ";

    /// <summary>
    /// پیشنهاد املای درست برای واژهٔ ناشناخته: پربسامدترین واژه با فاصلهٔ ویرایشی یک (حذف، افزودن، جابه‌جایی یا تعویض
    /// یک حرف). فقط واژهٔ فارسیِ حداقل ۴ حرفی که شناخته نیست و پیشنهادی با بسامد بالا (≥ ۴) دارد.
    /// مثال: «کیفیتت» → null (پسوند دارد)؛ «حتما» شناخته است؛ «مشخصاتت» → null؛ «پاوربانگ» → «پاوربانک» (از فهرست غلط‌ها).
    /// </summary>
    public string? SuggestSpelling(string word, IReadOnlySet<string>? siteVocabulary = null)
    {
        var normalized = PersianText.Normalize(word);
        if (normalized.Length < 4 || !normalized.All(PersianText.IsPersianLetter) || IsKnownWord(normalized)
            || siteVocabulary?.Contains(normalized) == true)
            return null;

        string? best = null;
        var bestScore = 4.0;
        void Consider(string candidate)
        {
            var score = _frequency.GetValueOrDefault(candidate);
            if (score > bestScore || score == bestScore && best is not null && string.CompareOrdinal(candidate, best) < 0)
            {
                best = candidate;
                bestScore = score;
            }
        }

        for (var i = 0; i < normalized.Length; i++)
        {
            Consider(normalized.Remove(i, 1));
            if (i < normalized.Length - 1)
                Consider(string.Concat(normalized.AsSpan(0, i), normalized[i + 1].ToString(), normalized[i].ToString(), normalized.AsSpan(i + 2)));
            foreach (var letter in PersianLetters)
                if (letter != normalized[i])
                    Consider(string.Concat(normalized.AsSpan(0, i), letter.ToString(), normalized.AsSpan(i + 1)));
        }
        for (var i = 0; i <= normalized.Length; i++)
            foreach (var letter in PersianLetters)
                Consider(normalized.Insert(i, letter.ToString()));
        return best;
    }

    // ── دانش آموخته ──────────────────────────────────────────────────────

    /// <summary>
    /// عبارت‌های جست‌وجوی واقعی که قبلاً برای این موضوع (یا موضوع‌های شامل آن) پیدا شده‌اند؛ پرامتیازها اول.
    /// مثال: PhrasesFor("هدفون سونی") → [«هدفون سونی wh-1000xm5»، «قیمت هدفون سونی» …].
    /// </summary>
    public IReadOnlyList<KnowledgeRule> PhrasesFor(string topic, int take = 30)
    {
        var key = PersianText.Normalize(topic);
        if (key.Length == 0)
            return [];
        if (_phrases.TryGetValue(key, out var exact))
            return exact.Where(r => r.Confidence >= 0.4).OrderByDescending(r => r.Confidence * (1 + Math.Log10(1 + r.Weight))).Take(take).ToList();
        var stems = PersianText.ContentStems(key).ToHashSet(StringComparer.Ordinal);
        if (stems.Count == 0)
            return [];
        var needed = Math.Max(1, (int)Math.Ceiling(stems.Count * 0.6));
        return TopicStems
            .Where(t => t.Stems.Count(stems.Contains) >= needed)
            .SelectMany(t => _phrases[t.Key])
            .Where(r => r.Confidence >= 0.4)
            .OrderByDescending(r => r.Confidence * (1 + Math.Log10(1 + r.Weight)))
            .DistinctBy(r => r.Term)
            .Take(take)
            .ToList();
    }

    /// <summary>
    /// ریشه‌های هر موضوع (یک بار ساخته می‌شود). چرا: موتور برای هر صفحه PhrasesFor را صدا می‌زند و محاسبهٔ دوبارهٔ
    /// ریشه‌های هزاران موضوع در هر بار، اجرای کامل را کند می‌کرد.
    /// </summary>
    private IReadOnlyList<(string Key, string[] Stems)> TopicStems => _topicStems ??=
        _phrases.Keys.Select(k => (k, PersianText.ContentStems(k).ToArray())).Where(t => t.Item2.Length > 0).ToList();

    private IReadOnlyList<(string Key, string[] Stems)>? _topicStems;

    /// <summary>آمار یادگیری یک کلمهٔ کلیدی (پذیرش/رد قبلی کارشناس). null = تجربه‌ای نداریم.</summary>
    public KnowledgeRule? KeywordHistory(string phrase) => _keywords.GetValueOrDefault(PersianText.Normalize(phrase));

    /// <summary>
    /// امتیاز یادگیری برای رتبه‌بندی کلمه‌های نامزد: پذیرش‌های قبلی مثبت و ردها منفی. مثال: ۳ پذیرش ← +۱٫۸؛ ۲ رد ← −۲٫۴.
    /// </summary>
    public double KeywordBonus(string phrase)
    {
        var history = KeywordHistory(phrase);
        if (history is null)
            return 0;
        return Math.Round(0.9 * Math.Log2(1 + history.Successes) - 1.2 * history.Rejections, 2);
    }
}
