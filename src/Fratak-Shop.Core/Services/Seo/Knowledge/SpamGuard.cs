using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Knowledge;

/// <summary>یک خطر جریمه یا اسپم. Severity: ۱ = هشدار، ۲ = جدی (خروجی خودکار اصلاح یا حذف می‌شود).</summary>
public sealed record SpamIssue(string Code, string Message, int Severity);

/// <summary>
/// نگهبان «جریمه نشدن در گوگل» (مرحلهٔ شانزدهم). هر متنی که موتور سئو یا «پیشنهاد هوشمند» می‌سازد پیش از نمایش
/// از این کلاس می‌گذرد:
/// <list type="bullet">
/// <item>عبارت‌های اغراق‌آمیز و اسپم (ارزان‌ترین، فقط امروز، !!!، شکلک در عنوان) — از جدول دانش (نوع Spam).</item>
/// <item>انباشت کلمهٔ کلیدی (Keyword stuffing): بیش از یک بار در عنوان، بیش از دو بار در توضیحات متا، تراکم بالای ۳٪ در متن.</item>
/// <item>تکرار ملال‌آور یک واژه، جملهٔ تکراری، پررنگ کردن بیش از حد، متن پنهان (display:none، اندازهٔ صفر) و فهرست کلمات کلیدی طولانی.</item>
/// </list>
/// همهٔ این‌ها همان چیزهایی است که راهنمای اسپم گوگل (Spam policies) و مرچنت سنتر صریحاً جریمه می‌کنند.
/// </summary>
public static partial class SpamGuard
{
    /// <summary>بیشترین تراکم مجاز کلمهٔ کلیدی در متن (درصد واژه‌ها). بالاتر از این «انباشت» حساب می‌شود.</summary>
    public const double MaxDensity = 3.0;

    /// <summary>تراکم مطلوب (برای نوشتن): حدود ۰٫۵ تا ۲٫۵ درصد.</summary>
    public const double TargetDensity = 2.5;

    /// <summary>حداکثر تعداد پررنگ (strong/b) در یک متن.</summary>
    public const int MaxBold = 12;

    /// <summary>فهرست پیش‌فرض اسپم وقتی دانش در دسترس نیست (کوتاه؛ فهرست کامل در جدول دانش است).</summary>
    private static readonly string[] FallbackSpam = ["ارزان‌ترین", "ارزانترین", "فقط امروز", "عجله کنید", "کلیک کنید", "!!!", "باورنکردنی", "بهترین قیمت"];

    /// <summary>عبارت کوتاه اسپم است؟ (برای فیلتر کردن عبارت‌های تحقیق پیش از ذخیره در جدول دانش)</summary>
    public static bool IsSpammy(string phrase, SeoKnowledgeBase? knowledge = null)
    {
        if (Emoji().IsMatch(phrase) || RepeatedPunctuation().IsMatch(phrase))
            return true;
        var normalized = PersianText.Normalize(phrase);
        var list = knowledge?.Spam.Where(r => r.Weight >= 0.8).Select(r => r.Term) ?? FallbackSpam;
        return list.Any(term => Contains(normalized, PersianText.Normalize(term)));
    }

    /// <summary>
    /// پاک کردن عنوان از اسپم: عبارت‌های فهرست Spam (جای title و any)، شکلک‌ها، علامت‌های تکراری و حروف بزرگ تمام‌کلمه.
    /// مثال: «🔥 ارزان‌ترین هدفون سونی!!!» → «هدفون سونی».
    /// </summary>
    public static string CleanTitle(string? title, SeoKnowledgeBase? knowledge, List<string>? removed = null) =>
        Clean(title, knowledge, titleScope: true, removed);

    /// <summary>پاک کردن متن معمولی (توضیحات، متا): فقط عبارت‌هایی که «همه‌جا» اسپم‌اند (جای any).</summary>
    public static string CleanText(string? text, SeoKnowledgeBase? knowledge, List<string>? removed = null) =>
        Clean(text, knowledge, titleScope: false, removed);

    private static string Clean(string? text, SeoKnowledgeBase? knowledge, bool titleScope, List<string>? removed)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var result = text;
        if (titleScope && Emoji().IsMatch(result))
        {
            removed?.Add("شکلک");
            result = Emoji().Replace(result, " ");
        }
        if (RepeatedPunctuation().IsMatch(result))
        {
            removed?.Add("علامت تکراری");
            result = RepeatedPunctuation().Replace(result, m => m.Value[..1]);
        }

        var rules = knowledge?.Spam.Where(r => titleScope || r.Topic == "any") ?? FallbackSpam.Select(s => new KnowledgeRule(SeoKnowledgeKind.Spam, s, null, "any", 1));
        foreach (var rule in rules)
        {
            if (rule.Term.Length == 0 || rule.Weight < 0.6)
                continue;
            var replaced = ReplacePhrase(result, rule.Term, rule.Value ?? "");
            if (!ReferenceEquals(replaced, result) && replaced != result)
            {
                removed?.Add(rule.Term);
                result = replaced;
            }
        }

        if (titleScope)
            result = Shouting().Replace(result, m => m.Value.Length <= 4 ? m.Value : m.Value[0] + m.Value[1..].ToLowerInvariant());
        result = DanglingSeparators().Replace(PersianText.Clean(result), " ").Trim(' ', '-', '|', '،', ',', ':', '–');
        return PersianText.Clean(result);
    }

    /// <summary>
    /// جایگزینی عبارت با رعایت مرز واژه (نیم‌فاصله و فاصله یکسان دیده می‌شوند). مثال: ReplacePhrase("خرید ارزان‌ترین هدفون", "ارزان‌ترین", "")
    /// → «خرید هدفون». اگر چیزی پیدا نشد، همان رشتهٔ ورودی (همان شیء) برمی‌گردد.
    /// </summary>
    public static string ReplacePhrase(string text, string phrase, string replacement)
    {
        var pattern = PhrasePattern(phrase);
        if (pattern is null)
            return text;
        try
        {
            var regex = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
            if (!regex.IsMatch(text))
                return text;
            return MultiSpace().Replace(regex.Replace(text, replacement.Length == 0 ? " " : replacement), " ").Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return text;
        }
    }

    /// <summary>الگوی Regex برای یک عبارت: فاصله/نیم‌فاصله قابل جابه‌جایی؛ ی/ي و ک/ك یکسان؛ مرز واژه در دو طرف (اگر عبارت با حرف شروع/تمام شود).</summary>
    public static string? PhrasePattern(string phrase)
    {
        phrase = phrase.Trim();
        if (phrase.Length == 0)
            return null;
        var builder = new System.Text.StringBuilder();
        if (char.IsLetterOrDigit(phrase[0]))
            builder.Append(@"(?<![\p{L}\p{N}\u200C])");
        foreach (var ch in phrase)
        {
            builder.Append(ch switch
            {
                ' ' or '\u200C' => @"[\s\u200C]*",
                'ی' or 'ي' or 'ى' => "[یيى]",
                'ک' or 'ك' => "[کك]",
                _ => Regex.Escape(ch.ToString()),
            });
        }
        if (char.IsLetterOrDigit(phrase[^1]))
            builder.Append(@"(?![\p{L}\p{N}\u200C])");
        return builder.ToString();
    }

    private static bool Contains(string normalizedText, string normalizedPhrase) =>
        normalizedPhrase.Length > 0 && (" " + normalizedText + " ").Contains(char.IsLetterOrDigit(normalizedPhrase[0]) ? " " + normalizedPhrase + " " : normalizedPhrase, StringComparison.Ordinal)
        || normalizedPhrase.Length > 0 && !char.IsLetterOrDigit(normalizedPhrase[0]) && normalizedText.Contains(normalizedPhrase, StringComparison.Ordinal);

    /// <summary>
    /// تراکم کلمهٔ کلیدی (درصد): (دفعات تکرار × تعداد واژه‌های کلمه) ÷ کل واژه‌ها × ۱۰۰. مثال: ۴ بار «هدفون سونی» در ۲۰۰ واژه → ۴٪.
    /// </summary>
    public static double KeywordDensity(string? plainText, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(plainText) || string.IsNullOrWhiteSpace(keyword))
            return 0;
        var words = PersianText.WordCount(plainText);
        var keywordWords = Math.Max(1, PersianText.ContentStems(keyword).Count);
        if (words == 0)
            return 0;
        return Math.Round(KeywordMatcher.Count(plainText, keyword) * keywordWords * 100.0 / words, 2);
    }

    /// <summary>
    /// بررسی کامل خطر جریمه برای یک صفحه. هر ورودی می‌تواند خالی باشد.
    /// مثال: عنوان «هدفون سونی | خرید هدفون سونی | هدفون سونی ارزان» → keyword-title (کلمه سه بار در عنوان).
    /// </summary>
    public static List<SpamIssue> Inspect(string? title, string? meta, string? bodyHtml, string? keyword, SeoKnowledgeBase? knowledge, string? metaKeywords = null)
    {
        var issues = new List<SpamIssue>();
        var hasKeyword = !string.IsNullOrWhiteSpace(keyword);

        if (!string.IsNullOrWhiteSpace(title))
        {
            if (hasKeyword && KeywordMatcher.Count(title, keyword!) > 1)
                issues.Add(new("keyword-title", "کلمهٔ کلیدی بیش از یک بار در عنوان آمده (انباشت کلمه؛ گوگل آن را اسپم می‌داند).", 2));
            if (IsSpammy(title, knowledge) || CleanTitle(title, knowledge) != PersianText.Clean(title))
                issues.Add(new("spam-title", "عنوان عبارت اغراق‌آمیز، شکلک یا علامت تکراری دارد (مثل «ارزان‌ترین» یا «!!!»).", 2));
        }

        if (!string.IsNullOrWhiteSpace(meta))
        {
            if (hasKeyword && KeywordMatcher.Count(meta, keyword!) > 2)
                issues.Add(new("keyword-meta", "کلمهٔ کلیدی بیش از دو بار در توضیحات متا تکرار شده.", 2));
            if (CleanText(meta, knowledge) != PersianText.Clean(meta))
                issues.Add(new("spam-meta", "توضیحات متا عبارت اغراق‌آمیز یا علامت تکراری دارد.", 1));
        }

        if (!string.IsNullOrWhiteSpace(metaKeywords))
        {
            var count = metaKeywords.Split([',', '،'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            if (count > 10)
                issues.Add(new("too-many-keywords", $"فهرست کلمات کلیدی {PersianText.Digits(count)} عبارت دارد؛ بیش از ۱۰ عبارت نشانهٔ انباشت است.", 1));
        }

        if (!string.IsNullOrWhiteSpace(bodyHtml))
        {
            if (HiddenText().IsMatch(bodyHtml))
                issues.Add(new("hidden-text", "متن پنهان (display:none، اندازهٔ صفر یا رنگ نامرئی) پیدا شد؛ گوگل این را فریب می‌داند و جریمه می‌کند.", 2));
            var bold = BoldTag().Matches(bodyHtml).Count;
            if (bold > MaxBold)
                issues.Add(new("over-bold", $"{PersianText.Digits(bold)} بخش پررنگ شده؛ پررنگ کردن زیاد ارزش تأکید را از بین می‌برد.", 1));

            var text = PersianText.HtmlToText(bodyHtml);
            var words = PersianText.WordCount(text);
            if (hasKeyword && words >= 80)
            {
                var density = KeywordDensity(text, keyword);
                if (density > MaxDensity)
                    issues.Add(new("density", $"تراکم کلمهٔ کلیدی {PersianText.Decimal1(density)}٪ است (بیش از {PersianText.Digits((long)MaxDensity)}٪ = انباشت کلمه).", 2));
            }
            if (words >= 150)
            {
                var top = PersianText.Tokenize(text)
                    .Where(t => t.Length > 2 && !PersianText.IsStopWord(t) && !(knowledge?.IsStopWord(t) ?? false) && !t.All(char.IsDigit))
                    .GroupBy(t => t)
                    .Select(g => (Word: g.Key, Share: g.Count() * 100.0 / words))
                    .OrderByDescending(x => x.Share)
                    .FirstOrDefault();
                if (top.Word is not null && top.Share > 6 && (!hasKeyword || !PersianText.Tokenize(keyword).Contains(top.Word)))
                    issues.Add(new("repetition", $"واژهٔ «{top.Word}» {PersianText.Decimal1(top.Share)}٪ متن است؛ تکرار زیاد متن را ماشینی نشان می‌دهد.", 1));
            }
            var sentences = PersianText.Sentences(text).Where(s => PersianText.WordCount(s) >= 6).Select(PersianText.Normalize).ToList();
            var duplicate = sentences.GroupBy(s => s).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
                issues.Add(new("duplicate-sentence", "یک جمله چند بار در متن تکرار شده است.", 1));
        }
        return issues;
    }

    [GeneratedRegex(@"[\u2600-\u27BF\u2B00-\u2BFF\u2300-\u23FF]|\uD83C[\uDC00-\uDFFF]|\uD83D[\uDC00-\uDFFF]|\uD83E[\uDD00-\uDFFF]|\uFE0F")]
    private static partial Regex Emoji();

    [GeneratedRegex(@"([!؟?*★☆.،,])\1{1,}")]
    private static partial Regex RepeatedPunctuation();

    [GeneratedRegex(@"\b[A-Z]{5,}\b")]
    private static partial Regex Shouting();

    [GeneratedRegex(@"(\s*[-|–]\s*){2,}")]
    private static partial Regex DanglingSeparators();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"display\s*:\s*none|visibility\s*:\s*hidden|font-size\s*:\s*0(px|pt|em|rem)?\s*[;""']|color\s*:\s*(#fff(fff)?|white|transparent)\s*;?\s*background(-color)?\s*:\s*(#fff(fff)?|white)", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenText();

    [GeneratedRegex(@"<(strong|b)(\s[^>]*)?>", RegexOptions.IgnoreCase)]
    private static partial Regex BoldTag();
}
