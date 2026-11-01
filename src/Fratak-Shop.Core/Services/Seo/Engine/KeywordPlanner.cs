using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  برنامه‌ریز کلمهٔ کلیدی و مشاور نامک
//  • KeywordPlanner: برای هر صفحه چند «کلمهٔ کلیدی نامزد» می‌سازد و با این نشانه‌ها امتیاز می‌دهد:
//      - تقاضای واقعی: عبارت در پیشنهادهای جست‌وجوی گوگل/بینگ هست؟ (هرچه بالاتر، بهتر)
//      - جست‌وجوی داخلی سایت (جدول SearchQueryLog): مشتری‌های خودمان این را جست‌وجو کرده‌اند؟
//      - رقبا: عنوان صفحه‌های برتر این عبارت را دارند؟
//      - دقیق بودن: برای محصول باید مدل/برند را داشته باشد (کلمهٔ عمومی مثل «گوشی موبایل» رقابت داخلی می‌سازد).
//      - طول مناسب (۲ تا ۵ واژه) و نبودن در صفحهٔ دیگر سایت (رقابت داخلی).
//  • SlugAdvisor: فارسی یا انگلیسی بودن نامک را از روی نامک صفحه‌های برتر نتایج انتخاب می‌کند و فقط وقتی
//    تغییر نامک را پیشنهاد می‌دهد که صفحه تازه یا کم‌بازدید باشد (همیشه با ریدایرکت ۳۰۱).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک کلمهٔ کلیدی نامزد با امتیاز و دلیل‌ها (دلیل‌ها در صفحهٔ بررسی به کارشناس نشان داده می‌شود).</summary>
public sealed record KeywordCandidate(string Phrase, double Score, IReadOnlyList<string> Reasons);

/// <summary>
/// یک عبارت جست‌وجوی واقعی گوگل که همین صفحه برایش نمایش داده شده (از Search Console، ۲۸ روز اخیر).
/// منبع: جدول SearchConsoleQueryStats؛ ساخته‌شده در SeoEngineService.BuildModelsAsync. مثال: («قیمت گوشی a55»، ۱۲ کلیک، ۸۴۰ نمایش، جایگاه ۷٫۴).
/// </summary>
public sealed record SearchQueryStat(string Query, int Clicks, int Impressions, double Position);

/// <summary>برنامه‌ریز کلمهٔ کلیدی (ایستا).</summary>
public static class KeywordPlanner
{
    // واژه‌هایی که بعدشان معمولاً جزئیات فنی می‌آید و برای «نام کوتاه» محصول بریده می‌شوند.
    // مثال: «گوشی موبایل سامسونگ مدل Galaxy A55 ظرفیت 256 گیگابایت» → «گوشی موبایل سامسونگ مدل Galaxy A55».
    private static readonly HashSet<string> SpecMarkers = new(StringComparer.Ordinal)
    {
        "ظرفیت", "رم", "حافظه", "همراه", "به", "دو", "تک", "مناسب", "مخصوص", "سایز", "اندازه", "رنگ", "طرح", "بسته", "پک", "ست",
    };

    // واژه‌های تجاری: در عنوان خوب‌اند ولی کلمهٔ کلیدی اصلی محصول بهتر است بدون آن‌ها باشد.
    private static readonly HashSet<string> CommercialWords = new(StringComparer.Ordinal) { "خرید", "قیمت", "فروش", "ارزان", "تخفیف", "بهترین" };

    /// <summary>
    /// نام کوتاه محصول با همان املای اصلی (برای عنوان و متن). بعد از حداقل ۳ واژه، از اولین نشانهٔ جزئیات بریده می‌شود
    /// و حداکثر ۷ واژه می‌ماند. مثال: «هدفون بی‌سیم سونی مدل WH-1000XM5 (اصلی)» → «هدفون بی‌سیم سونی مدل WH-1000XM5».
    /// </summary>
    public static string ShortName(string name)
    {
        var clean = PersianText.Clean(name);
        foreach (var stop in new[] { " (", "(", " - ", " | ", "|", "،", "," , " + " })
        {
            var index = clean.IndexOf(stop, StringComparison.Ordinal);
            if (index >= 12)
                clean = clean[..index];
        }

        var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>();
        foreach (var word in words)
        {
            if (kept.Count >= 3 && SpecMarkers.Contains(PersianText.Normalize(word)))
                break;
            kept.Add(word);
            if (kept.Count >= 7)
                break;
        }
        return string.Join(' ', kept).Trim('،', ',', '-', ' ');
    }

    /// <summary>
    /// عبارتی که برای تحقیق (پیشنهاد جست‌وجو و نتایج برتر) جست‌وجو می‌شود.
    /// محصول: نام کوتاه بدون «مدل»؛ دسته/برند: نام؛ مقاله/صفحه: کلمهٔ کلیدی فعلی یا عنوان.
    /// </summary>
    public static string ResearchQuery(SeoSubject subject)
    {
        if (!string.IsNullOrWhiteSpace(subject.FocusKeyword))
            return PersianText.Clean(subject.FocusKeyword);
        return subject.Kind switch
        {
            SeoEntityKind.Product => RemoveWord(ShortName(subject.Name), "مدل"),
            SeoEntityKind.Brand => subject.Name,
            SeoEntityKind.BlogPost => PersianText.Truncate(subject.Name, 70).TrimEnd('…'),
            _ => subject.Name,
        };
    }

    /// <summary>
    /// کلمه‌های نامزد به ترتیب امتیاز. internalSearches: عبارت‌های نرمال‌شدهٔ جست‌وجوی داخلی → تعداد.
    /// tfidf (اختیاری) برای مقاله‌ها: واژه‌های مهم متن نسبت به کل سایت.
    /// </summary>
    public static List<KeywordCandidate> Candidates(SeoSubject subject, CompetitorInsight? insight, SeoSiteIndex? site,
        IReadOnlyDictionary<string, int>? internalSearches, TfIdf? tfidf = null, IReadOnlyList<SearchQueryStat>? gsc = null,
        Knowledge.SeoKnowledgeBase? knowledge = null)
    {
        var phrases = new Dictionary<string, string>(StringComparer.Ordinal); // نرمال → نمایشی
        void Add(string? phrase)
        {
            var clean = PersianText.Clean(phrase).Trim('،', ',', '-', ':', '؛', ' ');
            var key = PersianText.Normalize(clean);
            var count = PersianText.Tokenize(clean).Count;
            if (key.Length >= 2 && count is >= 1 and <= 7 && clean.Length <= 60)
                phrases.TryAdd(key, clean);
        }

        var nameTokens = PersianText.Tokenize(subject.Kind == SeoEntityKind.Product ? ShortName(subject.Name) : subject.Name);
        switch (subject.Kind)
        {
            case SeoEntityKind.Product:
                var shortName = ShortName(subject.Name);
                Add(shortName);
                Add(RemoveWord(shortName, "مدل"));
                // پنجره‌های پیوسته از نام (۲ تا ۵ واژه) تا کلمه دقیقاً در نام و متن تکرار شود.
                var tokens = PersianText.Tokenize(RemoveWord(shortName, "مدل"));
                for (var length = 2; length <= Math.Min(5, tokens.Count); length++)
                    for (var start = 0; start + length <= tokens.Count; start++)
                        Add(string.Join(' ', tokens.Skip(start).Take(length)));
                if (subject.Product?.BrandName is { } brand)
                {
                    var model = ModelTokens(subject.Name);
                    if (model.Count > 0)
                    {
                        Add(brand + " " + string.Join(' ', model));
                        var type = TypeWord(subject.Name);
                        if (type is not null)
                            Add(type + " " + brand + " " + string.Join(' ', model));
                    }
                }
                break;
            case SeoEntityKind.Category:
                Add(subject.Name);
                Add("خرید " + subject.Name);
                Add("قیمت " + subject.Name);
                break;
            case SeoEntityKind.Brand:
                Add(subject.Name);
                Add(subject.EnglishName);
                Add("محصولات " + subject.Name);
                break;
            case SeoEntityKind.BlogPost:
                // عبارت‌های ۲ تا ۴ واژه‌ای پیوستهٔ عنوان که با واژهٔ ایست شروع یا تمام نمی‌شوند.
                var titleTokens = PersianText.Tokenize(subject.Name);
                for (var length = 2; length <= Math.Min(4, titleTokens.Count); length++)
                    for (var start = 0; start + length <= titleTokens.Count; start++)
                    {
                        var window = titleTokens.Skip(start).Take(length).ToList();
                        if (!PersianText.IsStopWord(window[0]) && !PersianText.IsStopWord(window[^1]))
                            Add(string.Join(' ', window));
                    }
                break;
            default:
                Add(subject.Name);
                break;
        }

        // پیشنهادهای جست‌وجو فقط وقتی مرتبط‌اند که واژه‌های هویتی صفحه را داشته باشند.
        foreach (var suggestion in insight?.Suggestions ?? [])
            if (IsRelevant(subject, suggestion, nameTokens))
                Add(suggestion);

        // عبارت‌هایی که گوگل واقعاً این صفحه را برایشان نشان داده (Search Console): دقیق‌ترین نشانهٔ تقاضا.
        // مرحلهٔ شانزدهم: عبارت‌هایی که تحقیق‌های قبلی برای همین موضوع پیدا کرده‌اند (جدول دانش؛ با هر تحقیق بیشتر می‌شود).
        var learned = knowledge?.PhrasesFor(ResearchQuery(subject)) ?? [];
        foreach (var phrase in learned)
            if (IsRelevant(subject, phrase.Term, nameTokens))
                Add(phrase.Term);

        var googleQueries = (gsc ?? [])
            .Select(q => (Key: PersianText.Normalize(q.Query), Stat: q))
            .Where(q => q.Key.Length > 0)
            .ToList();
        foreach (var (_, stat) in googleQueries)
            if (IsRelevant(subject, stat.Query, nameTokens))
                Add(stat.Query);

        var bodyText = subject.Kind == SeoEntityKind.BlogPost ? PersianText.HtmlToText(subject.BodyHtml) : null;
        var topStems = bodyText is not null && tfidf is not null
            ? tfidf.Top(bodyText, 15).ToDictionary(t => t.Stem, t => t.Weight, StringComparer.Ordinal)
            : null;

        var result = new List<KeywordCandidate>();
        foreach (var (key, phrase) in phrases)
        {
            if (!IsRelevant(subject, phrase, nameTokens) || knowledge is not null && Knowledge.SpamGuard.IsSpammy(phrase, knowledge))
                continue;
            var reasons = new List<string>();
            var score = 0.0;
            var words = PersianText.Tokenize(phrase);

            // طول: ۳-۴ واژه بهترین تعادل «تقاضا/رقابت» برای محصول؛ دسته و برند کوتاه‌تر.
            score += subject.Kind is SeoEntityKind.Product or SeoEntityKind.BlogPost
                ? words.Count switch { 1 => -2, 2 => 0.6, 3 => 1, 4 => 1, 5 => 0.6, 6 => 0.1, _ => -1 }
                : words.Count switch { 1 => 0.8, 2 => 1, 3 => 0.8, 4 => 0.4, _ => -0.5 };

            // پیشنهاد جست‌وجو = تقاضای واقعی
            var suggestions = insight?.Suggestions ?? [];
            var exact = suggestions.FindIndex(s => PersianText.Normalize(s) == key);
            if (exact >= 0)
            {
                score += 3 + (10 - Math.Min(10, exact)) / 10.0;
                reasons.Add("در پیشنهادهای جست‌وجوی گوگل/بینگ هست (تقاضای واقعی)");
            }
            else if (suggestions.Any(s => PersianText.Normalize(s).StartsWith(key + " ", StringComparison.Ordinal)))
            {
                score += 1.5;
                reasons.Add("ابتدای چند عبارت پرجست‌وجوست");
            }

            // دادهٔ واقعی گوگل: لگاریتم نمایش (۱۰۰ نمایش ≈ ۵ امتیاز) + کمی برای کلیک.
            // مثال: ۸۴۰ نمایش و ۱۲ کلیک → ۲٫۵×۲٫۹۲ + ۱٫۲ ≈ ۸٫۵ (از پیشنهاد جست‌وجو قوی‌تر چون تقاضای ثابت‌شدهٔ همین صفحه است).
            var google = googleQueries.Where(q => q.Key == key).Select(q => q.Stat).FirstOrDefault();
            if (google is not null && google.Impressions > 0)
            {
                score += 2.5 * Math.Log10(1 + google.Impressions) + Math.Min(2, google.Clicks / 10.0);
                reasons.Add($"در گوگل {PersianText.Digits(google.Impressions)} بار نمایش داده شده (جایگاه میانگین {PersianText.Decimal1(google.Position)})");
            }
            else if (googleQueries.Count > 0)
            {
                var within = googleQueries.Where(q => q.Key.Contains(key, StringComparison.Ordinal)).Sum(q => q.Stat.Impressions);
                if (within > 0)
                {
                    score += Math.Log10(1 + within);
                    reasons.Add($"بخشی از عبارت‌هایی است که در گوگل {PersianText.Digits(within)} بار نمایش داده شده‌اند");
                }
            }

            if (internalSearches is not null)
            {
                var hits = internalSearches.Where(s => s.Key == key || s.Key.Contains(key, StringComparison.Ordinal)).Sum(s => s.Value);
                if (hits > 0)
                {
                    score += 2 * Math.Log10(1 + hits);
                    reasons.Add($"مشتری‌های سایت {PersianText.Digits(hits)} بار جست‌وجو کرده‌اند");
                }
            }

            if (insight is { SampleTitles.Count: > 0 })
            {
                var inTitles = insight.SampleTitles.Count(t => KeywordMatcher.Contains(t, phrase));
                if (inTitles > 0)
                {
                    score += 1.2 * inTitles / insight.SampleTitles.Count;
                    reasons.Add($"در عنوان {PersianText.Digits(inTitles)} نتیجهٔ برتر آمده");
                }
            }

            if (subject.Kind == SeoEntityKind.Product)
            {
                // پوشش نام: هرچه بیشتر از نام را داشته باشد، صفحه دقیق‌تر به جست‌وجو جواب می‌دهد.
                var nameStems = PersianText.ContentStems(subject.Name).ToHashSet(StringComparer.Ordinal);
                var stems = PersianText.ContentStems(phrase);
                if (nameStems.Count > 0)
                    score += 1.5 * stems.Count(nameStems.Contains) / Math.Min(nameStems.Count, 5.0);
                if (words.Any(w => CommercialWords.Contains(w)))
                    score -= 0.8;
            }

            if (topStems is not null)
            {
                var stems = PersianText.ContentStems(phrase);
                var weight = stems.Sum(s => topStems.GetValueOrDefault(s));
                score += Math.Min(2, weight * 8);
                var inBody = KeywordMatcher.Count(bodyText!, phrase);
                if (inBody >= 2)
                {
                    score += 1;
                    reasons.Add($"{PersianText.Digits(inBody)} بار در متن مقاله آمده");
                }
                else if (inBody == 0)
                    score -= 1.5;
            }

            if (knowledge is not null)
            {
                // تقاضای پایدار: عبارتی که در چند تحقیق جداگانه دوباره پیدا شده (وزن = دفعات).
                var memory = learned.FirstOrDefault(l => PersianText.Normalize(l.Term) == key);
                if (memory is not null)
                {
                    score += Math.Min(1.5, 0.6 + 0.5 * Math.Log10(1 + memory.Weight));
                    reasons.Add(memory.Weight >= 2
                        ? $"در {PersianText.Digits((int)memory.Weight)} تحقیق قبلی هم پیدا شده بود (تقاضای پایدار)"
                        : "در تحقیق‌های قبلی موتور هم پیدا شده بود");
                }
                // تجربهٔ کارشناس: کلمه‌ای که قبلاً تأیید شده جلو و کلمهٔ ردشده عقب می‌رود.
                var bonus = knowledge.KeywordBonus(phrase);
                if (bonus != 0)
                {
                    score += bonus;
                    reasons.Add(bonus > 0 ? "کارشناس قبلاً این کلمه را برای صفحه‌های مشابه تأیید کرده" : "این کلمه قبلاً رد شده است");
                }
            }

            if (site is not null && SeoSiteIndex.Others(site.FocusKeywords, phrase, subject.EntityId) > 0)
            {
                score -= 5;
                reasons.Add("صفحهٔ دیگری روی همین کلمه کار می‌کند (رقابت داخلی)");
            }

            result.Add(new KeywordCandidate(phrase, Math.Round(score, 3), reasons));
        }

        return result.OrderByDescending(c => c.Score).ThenBy(c => c.Phrase.Length).ThenBy(c => c.Phrase, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// برای محصول، عبارت باید مدل (واژهٔ لاتین/عددی نام) یا برند را داشته باشد؛ برای بقیه باید حداقل نیمی از واژه‌های نام را.
    /// مثال: برای «هدفون سونی WH-1000XM5»، «هدفون بی‌سیم» مرتبط حساب نمی‌شود (خیلی عمومی است).
    /// </summary>
    public static bool IsRelevant(SeoSubject subject, string phrase, IReadOnlyList<string> nameTokens)
    {
        var stems = PersianText.ContentStems(phrase).ToHashSet(StringComparer.Ordinal);
        if (stems.Count == 0)
            return false;
        if (subject.Kind == SeoEntityKind.Product)
        {
            var model = ModelTokens(subject.Name).Select(PersianText.Stem).ToList();
            var brand = PersianText.ContentStems(subject.Product?.BrandName).Concat(PersianText.ContentStems(subject.Product?.BrandEnglishName)).ToList();
            if (model.Count > 0)
                return model.Any(stems.Contains);
            if (brand.Count > 0 && brand.Any(stems.Contains))
                return true;
        }
        var nameStems = nameTokens.Where(t => !PersianText.IsStopWord(t)).Select(PersianText.Stem).Distinct(StringComparer.Ordinal).ToList();
        if (nameStems.Count == 0)
            return true;
        var covered = nameStems.Count(stems.Contains);
        return subject.Kind is SeoEntityKind.Category or SeoEntityKind.Brand
            ? covered == nameStems.Count || subject.EnglishName is not null && PersianText.ContentStems(subject.EnglishName).All(stems.Contains)
            : covered >= Math.Ceiling(nameStems.Count * (subject.Kind == SeoEntityKind.BlogPost ? 0.25 : 0.5));
    }

    /// <summary>
    /// انتخاب کلمه: بهترین نامزدی که قبلاً رد نشده. rejected: کلمه‌هایی که کارشناس قبلاً رد کرده.
    /// </summary>
    public static KeywordCandidate? Choose(IReadOnlyList<KeywordCandidate> candidates, IReadOnlyCollection<string> rejected)
    {
        var avoid = rejected.Select(PersianText.Normalize).ToHashSet(StringComparer.Ordinal);
        return candidates.FirstOrDefault(c => !avoid.Contains(PersianText.Normalize(c.Phrase)));
    }

    /// <summary>واژه‌های مدل: لاتین یا دارای رقم. مثال: «گوشی سامسونگ Galaxy A55» → [galaxy، a55].</summary>
    public static List<string> ModelTokens(string name) =>
        PersianText.Tokenize(ShortName(name))
            .Where(t => t.Any(char.IsDigit) || PersianText.PersianRatio(t) == 0 && t.Any(char.IsLetter))
            .Where(t => !PersianText.IsStopWord(t))
            .Take(4)
            .ToList();

    /// <summary>واژهٔ نوع کالا (اولین واژهٔ فارسی نام). مثال: «گوشی موبایل سامسونگ …» → «گوشی».</summary>
    public static string? TypeWord(string name) =>
        PersianText.Tokenize(name).FirstOrDefault(t => PersianText.PersianRatio(t) == 1 && !PersianText.IsStopWord(t));

    private static string RemoveWord(string text, string word) =>
        string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => PersianText.Normalize(w) != word));
}

/// <summary>پیشنهاد نامک تازه با دلیل.</summary>
public sealed record SlugAdvice(string Slug, string Reason);

/// <summary>
/// مشاور نامک. قانون‌ها (برای جلوگیری از افت رتبه):
///  ۱) فقط محصول و مقاله (نامک دسته/برند/صفحه‌ها در منوها و لینک‌ها استفاده می‌شود).
///  ۲) فقط صفحهٔ تازه (کمتر از ۱۴ روز) یا کم‌بازدید (کمتر از ۲۰ بازدید).
///  ۳) فقط وقتی نامک فعلی مشکل دارد (بی‌معنی، خیلی بلند، بدون کلمهٔ کلیدی) یا خطش با رقبا خیلی فرق دارد.
///  ۴) زبان: اگر ≥۶۰٪ نامک‌های نتایج برتر لاتین باشند و نام انگلیسی داریم → انگلیسی؛ اگر ≤۴۰٪ → فارسی؛ بین این دو → خط کلمهٔ کلیدی.
///  ۵) هنگام اعمال، ریدایرکت ۳۰۱ از آدرس قبلی ساخته می‌شود (SeoEngineService).
/// </summary>
public static class SlugAdvisor
{
    public static readonly TimeSpan YoungAge = TimeSpan.FromDays(14);
    public const int LowTrafficViews = 20;

    public static SlugAdvice? Advise(SeoSubject subject, string? keyword, CompetitorInsight? insight, SeoSiteIndex? site, DateTimeOffset now)
    {
        if (subject.Kind is not (SeoEntityKind.Product or SeoEntityKind.BlogPost))
            return null;
        var young = now - subject.CreatedAt < YoungAge;
        if (!young && subject.Views >= LowTrafficViews)
            return null;

        var current = Uri.UnescapeDataString(subject.Slug ?? "");
        var keywordText = PersianText.Clean(keyword);
        var english = !string.IsNullOrWhiteSpace(subject.EnglishName) ? subject.EnglishName
            : PersianText.PersianRatio(keywordText) < 0.5 && keywordText.Length > 0 ? keywordText : null;

        var preferLatin = PreferLatin(insight, keywordText);
        var source = preferLatin && english is not null
            ? english
            : keywordText.Length > 0 ? keywordText : KeywordPlanner.ShortName(subject.Name);
        var proposed = Limit(source.ToSlug());
        if (proposed.Length < 3 || string.Equals(proposed, current, StringComparison.OrdinalIgnoreCase))
            return null;

        var problems = new List<string>();
        var currentIsLatin = PersianText.PersianRatio(current) < 0.5;
        if (current.Length > SeoLimits.SlugMaxLength)
            problems.Add($"نامک فعلی بلند است ({PersianText.Digits(current.Length)} نویسه)");
        if (System.Text.RegularExpressions.Regex.IsMatch(current, @"(^|-)([0-9a-f]{10,}|[0-9]{8,})(-|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || current.All(c => char.IsDigit(c) || c == '-'))
            problems.Add("نامک فعلی بی‌معنی است");
        if (keywordText.Length > 0)
        {
            var keyStems = PersianText.ContentStems(keywordText);
            var slugStems = PersianText.ContentStems(current.Replace('-', ' '));
            var keywordIsLatin = PersianText.PersianRatio(keywordText) < 0.5;
            if (keyStems.Count > 0 && keyStems.Count(slugStems.Contains) == 0 && currentIsLatin == keywordIsLatin)
                problems.Add("نامک فعلی کلمهٔ کلیدی را ندارد");
        }
        var strongCompetitorSignal = insight is { SampleTitles.Count: >= 3 } && (insight.LatinSlugRatio >= 0.7 || insight.LatinSlugRatio <= 0.3);
        if (strongCompetitorSignal && young && currentIsLatin != (preferLatin && english is not null))
            problems.Add(preferLatin ? "بیشتر صفحه‌های برتر نامک انگلیسی دارند" : "بیشتر صفحه‌های برتر نامک فارسی دارند");

        if (problems.Count == 0)
            return null;

        // یکتا بودن در همان نوع صفحه: «-2»، «-3» …
        var unique = proposed;
        for (var i = 2; site is not null && site.Paths.Contains(subject.PathPrefix + unique) && i < 10; i++)
            unique = Limit(proposed) + "-" + i;
        if (site is not null && site.Paths.Contains(subject.PathPrefix + unique))
            return null;

        var age = young ? "صفحه تازه است" : $"صفحه کم‌بازدید است ({PersianText.Digits(subject.Views)} بازدید)";
        return new SlugAdvice(unique, $"{string.Join("؛ ", problems)}. {age} و تغییر نامک با ریدایرکت ۳۰۱ خودکار بی‌خطر است.");
    }

    /// <summary>
    /// نامک برای صفحه‌ای که هنوز نامک ندارد (ساخت محصول/مقالهٔ تازه در «سئوی هوشمند درجا»؛ مرحلهٔ شانزدهم).
    /// همان قانون‌های <see cref="Advise"/> برای خط (فارسی/انگلیسی)، طول و یکتایی، بدون شرط «مشکل داشتن نامک فعلی».
    /// مثال: کلمهٔ «هدفون سونی WH-1000XM5» و رقبای انگلیسی‌نامک → «sony-wh-1000xm5» (اگر نام انگلیسی ثبت شده باشد).
    /// </summary>
    public static SlugAdvice? ForNewPage(SeoSubject subject, string? keyword, CompetitorInsight? insight, SeoSiteIndex? site)
    {
        var keywordText = PersianText.Clean(keyword);
        var english = !string.IsNullOrWhiteSpace(subject.EnglishName) ? subject.EnglishName
            : PersianText.PersianRatio(keywordText) < 0.5 && keywordText.Length > 0 ? keywordText : null;
        var preferLatin = PreferLatin(insight, keywordText);
        var source = preferLatin && english is not null
            ? english
            : keywordText.Length > 0 ? keywordText : KeywordPlanner.ShortName(subject.Name);
        var proposed = Limit(source.ToSlug());
        if (proposed.Length < 3)
            return null;
        var unique = proposed;
        for (var i = 2; site is not null && site.Paths.Contains(subject.PathPrefix + unique) && i < 10; i++)
            unique = proposed + "-" + i;
        if (site is not null && site.Paths.Contains(subject.PathPrefix + unique))
            return null;
        var why = preferLatin && english is not null ? "بیشتر صفحه‌های برتر نامک انگلیسی دارند" : "نامک از کلمهٔ کلیدی ساخته شد";
        return new SlugAdvice(unique, why + "؛ کوتاه (حداکثر ۶ واژه) و یکتا در سایت.");
    }

    /// <summary>
    /// نامک انگلیسی بهتر است؟ بر اساس نتایج برتر؛ اگر داده نباشد یا مساوی باشد، خط کلمهٔ کلیدی تعیین می‌کند.
    /// </summary>
    public static bool PreferLatin(CompetitorInsight? insight, string keyword)
    {
        if (insight is { SampleTitles.Count: >= 3 })
        {
            if (insight.LatinSlugRatio >= 0.6) return true;
            if (insight.LatinSlugRatio <= 0.4) return false;
        }
        return keyword.Length > 0 && PersianText.PersianRatio(keyword) < 0.5;
    }

    /// <summary>حداکثر ۶ واژه و ۷۵ نویسه.</summary>
    private static string Limit(string slug)
    {
        var parts = slug.Split('-', StringSplitOptions.RemoveEmptyEntries).Take(6).ToList();
        var result = string.Join('-', parts);
        while (result.Length > SeoLimits.SlugMaxLength && parts.Count > 2)
        {
            parts.RemoveAt(parts.Count - 1);
            result = string.Join('-', parts);
        }
        return result;
    }
}
