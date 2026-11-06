using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Knowledge;

namespace Fratak_Shop.Core.Services.Seo.Smart;

/// <summary>یک تغییر نگارشی. RuleId برای یادگیری (مثلاً «7:باطری»؛ قاعده‌های داخلی با «b:» شروع می‌شوند و در جدول نیستند).</summary>
public sealed record WritingChange(string RuleId, string From, string To, string Category);

/// <summary>خروجی ویرایش نگارشی: متن تازه، فهرست تغییرها و یادداشت‌ها (پیشنهادهای کم‌اطمینان که اعمال نشدند).</summary>
public sealed class WritingResult
{
    public string Text { get; set; } = string.Empty;
    public List<WritingChange> Changes { get; } = [];
    public List<string> Notes { get; } = [];
}

/// <summary>کدام اصلاح‌ها انجام شود (پیش‌فرض: همه).</summary>
public sealed class WritingOptions
{
    /// <summary>محاوره به رسمی («میخوام» ← «می‌خواهم»).</summary>
    public bool Formal { get; init; } = true;
    /// <summary>غلط‌های املایی رایج («باطری» ← «باتری»).</summary>
    public bool Spelling { get; init; } = true;
    /// <summary>عبارت‌های زائد و حشو («لازم به ذکر است که» حذف).</summary>
    public bool Style { get; init; } = true;
    /// <summary>یکدست کردن واحدها («۲۵۶ گیگ» ← «۲۵۶ گیگابایت»).</summary>
    public bool Units { get; init; } = true;
    /// <summary>نیم‌فاصله و واژه‌های مرکب.</summary>
    public bool Spacing { get; init; } = true;
    /// <summary>رقم فارسی و نشانه‌گذاری فارسی.</summary>
    public bool Punctuation { get; init; } = true;
    /// <summary>پیشنهاد املایی برای واژه‌های ناشناخته (فقط یادداشت، هرگز خودکار).</summary>
    public bool SpellingNotes { get; init; } = true;
    /// <summary>واژه‌هایی که غلط حساب نمی‌شوند (نام محصول، برند، دسته). مثال: «شیائومی».</summary>
    public IReadOnlySet<string>? Vocabulary { get; init; }

    public static readonly WritingOptions Default = new();

    /// <summary>برای نام محصول و عنوان: بدون حذف حشو و بدون تبدیل رقم داخل نام مدل.</summary>
    public static readonly WritingOptions Title = new() { Style = false, SpellingNotes = true };
}

/// <summary>
/// ویراستار خودکار متن فارسی (مرحلهٔ شانزدهم) — رایگان و بدون هیچ سرویس بیرونی؛ همهٔ قاعده‌ها از جدول دانش
/// (<see cref="SeoKnowledgeBase"/>) خوانده می‌شود و با بازخورد کارشناس یاد می‌گیرد.
/// <para>کارها به ترتیب: یکسان‌سازی حروف (ي/ك)، محاوره ← رسمی، غلط‌های املایی رایج، واحدها، واژه‌های مرکب و
/// نیم‌فاصله (می‌/نمی‌، ـها، ـتر/ـترین، ـه‌ای)، حذف حشو، حذف واژهٔ تکراری، رقم و نشانه‌گذاری فارسی، گیومهٔ «».</para>
/// <para>مثال: «این گوشی خیلی خوبه و باطری اش 5000 میلی آمپر میباشد ,لازم به ذکر است که گارانتی داره»
/// ← «این گوشی خیلی خوب است و باتری‌اش ۵۰۰۰ میلی‌آمپر است، گارانتی دارد».</para>
/// استفاده: <see cref="TextRefiner"/> (همهٔ نسخه‌های «پیشنهاد هوشمند»)، سئوی درجا.
/// </summary>
public static partial class PersianWriting
{
    /// <summary>
    /// ویرایش متن ساده (نه HTML؛ برای HTML، <see cref="TextRefiner"/> فقط گره‌های متنی را به این متد می‌دهد).
    /// لاتین، نشانی اینترنتی، ایمیل و رقم‌های نام مدل (A55، WH-1000XM5) دست نمی‌خورند.
    /// </summary>
    public static WritingResult Polish(string? text, SeoKnowledgeBase knowledge, WritingOptions? options = null)
    {
        options ??= WritingOptions.Default;
        var result = new WritingResult();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var value = Letters(text);

        // بخش‌های محافظت‌شده (نشانی، ایمیل) موقتاً با نشانه جایگزین می‌شوند تا هیچ قاعده‌ای خرابشان نکند.
        var protectedParts = new List<string>();
        value = Protected().Replace(value, m =>
        {
            protectedParts.Add(m.Value);
            return "\uE000" + (char)(0xE100 + protectedParts.Count - 1) + "\uE001";
        });

        if (options.Formal)
            value = ApplyInformal(value, knowledge, result);
        if (options.Spelling)
            value = ApplyRules(value, knowledge.Corrections, result, "املا");
        if (options.Units)
            value = ApplyUnits(value, knowledge, result);
        if (options.Spacing)
        {
            value = ApplyCompounds(value, knowledge, result);
            value = ApplyZwnj(value, knowledge, result);
        }
        if (options.Style)
            value = ApplyRules(value, knowledge.Styles, result, "سبک");
        value = RemoveRepeatedWords(value, result);
        if (options.Punctuation)
            value = ApplyPunctuation(value, result);

        value = Tidy(value);
        for (var i = 0; i < protectedParts.Count; i++)
            value = value.Replace("\uE000" + (char)(0xE100 + i) + "\uE001", protectedParts[i]);
        result.Text = value;

        if (options.SpellingNotes)
            AddSpellingNotes(value, knowledge, options.Vocabulary, result);
        return result;
    }

    // ── حروف ───────────────────────────────────────────────────────────

    /// <summary>ي/ى ← ی، ك ← ک، حذف کشیده و فاصلهٔ نشکن، ارقام عربی ← فارسی. مثال: «كتابي» ← «کتابی».</summary>
    public static string Letters(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                'ي' or 'ى' => 'ی',
                'ك' => 'ک',
                '\u00A0' => ' ',
                >= '٠' and <= '٩' => (char)('۰' + (ch - '٠')),
                _ => ch,
            });
        }
        return builder.ToString().Replace("\u0640", "");
    }

    // ── قاعده‌های جدول دانش ───────────────────────────────────────────

    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new(StringComparer.Ordinal);

    /// <summary>Regex یک عبارت با مرز واژه (کش‌شده). null = عبارت نامعتبر.</summary>
    private static Regex? PhraseRegex(string phrase) => RegexCache.GetOrAdd(phrase, p =>
    {
        var pattern = SpamGuard.PhrasePattern(p);
        return pattern is null ? null : new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    });

    private static string ApplyRules(string value, IReadOnlyList<KnowledgeRule> rules, WritingResult result, string category)
    {
        foreach (var rule in rules)
        {
            var regex = PhraseRegex(rule.Term);
            if (regex is null)
                continue;
            try
            {
                if (!regex.IsMatch(value))
                    continue;
                var replacement = rule.Value ?? "";
                if (!rule.IsAuto)
                {
                    // اطمینان کم: فقط پیشنهاد (مثلاً «جهت» ← «برای» که همیشه درست نیست).
                    var reason = rule.Topic.Length > 0 && category == "سبک" ? $" ({rule.Topic})" : "";
                    result.Notes.Add(replacement.Length == 0
                        ? $"«{rule.Term}» را در صورت امکان حذف کنید{reason}."
                        : $"به‌جای «{rule.Term}» می‌توانید «{replacement}» بنویسید{reason}.");
                    continue;
                }
                value = regex.Replace(value, replacement);
                result.Changes.Add(new WritingChange(rule.Id, rule.Term, replacement, category));
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }
        return value;
    }

    /// <summary>محاوره ← رسمی؛ فقط وقتی شکل محاوره خودش یک واژهٔ رسمی پرکاربرد نباشد («تو» و «رو» دست نمی‌خورند).</summary>
    private static string ApplyInformal(string value, SeoKnowledgeBase knowledge, WritingResult result)
    {
        if (knowledge.Informals.Count == 0)
            return value;
        var safe = knowledge.Informals.Where(r => !string.IsNullOrEmpty(r.Value) && knowledge.Frequency(r.Term) < 4.0).ToList();
        return ApplyRulesFast(value, safe, result, "رسمی‌نویسی");
    }

    /// <summary>
    /// اعمال سریع قاعده‌های تک‌واژه‌ای با یک گذر روی واژه‌های متن (برای فهرست‌های چندصدتایی مثل محاوره).
    /// قاعده‌های چندواژه‌ای با Regex جداگانه اعمال می‌شوند.
    /// </summary>
    private static string ApplyRulesFast(string value, IReadOnlyList<KnowledgeRule> rules, WritingResult result, string category)
    {
        var single = new Dictionary<string, KnowledgeRule>(StringComparer.Ordinal);
        var multi = new List<KnowledgeRule>();
        foreach (var rule in rules)
        {
            if (rule.Term.Contains(' ') || rule.Term.Contains(PersianText.Zwnj))
                multi.Add(rule);
            else
                single.TryAdd(rule.Term, rule);
        }
        value = ApplyRules(value, multi, result, category);
        var applied = new HashSet<string>(StringComparer.Ordinal);
        value = PersianWord().Replace(value, m =>
        {
            if (!single.TryGetValue(m.Value, out var rule))
                return m.Value;
            if (!rule.IsAuto)
            {
                if (applied.Add("n:" + rule.Term))
                    result.Notes.Add($"به‌جای «{rule.Term}» می‌توانید «{rule.Value}» بنویسید.");
                return m.Value;
            }
            if (applied.Add(rule.Term))
                result.Changes.Add(new WritingChange(rule.Id, rule.Term, rule.Value ?? "", category));
            return rule.Value ?? "";
        });
        return value;
    }

    /// <summary>واحدها فقط بعد از عدد یکدست می‌شوند. مثال: «۲۵۶ گیگ» ← «۲۵۶ گیگابایت» ولی «گیگ» تنها دست نمی‌خورد.</summary>
    private static string ApplyUnits(string value, SeoKnowledgeBase knowledge, WritingResult result)
    {
        foreach (var rule in knowledge.Units)
        {
            if (string.IsNullOrEmpty(rule.Value) || !rule.IsAuto)
                continue;
            var inner = SpamGuard.PhrasePattern(rule.Term);
            if (inner is null)
                continue;
            try
            {
                // واحد لاتین (GB، mAh) فقط بعد از رقم فارسی (یعنی در متن فارسی)؛ «256 GB» در نام لاتین دست نمی‌خورد.
                var digit = rule.Term.Any(char.IsAsciiLetter) ? "[۰-۹]" : "[0-9۰-۹]";
                var regex = RegexCache.GetOrAdd("u:" + rule.Term, _ => new Regex("(?<=" + digit + " )" + inner, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))!;
                if (!regex.IsMatch(value))
                    continue;
                value = regex.Replace(value, rule.Value);
                result.Changes.Add(new WritingChange(rule.Id, rule.Term, rule.Value, "واحد"));
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }
        return value;
    }

    /// <summary>
    /// واژه‌های مرکب با یک گذر روی واژه‌ها (سه‌تایی، بعد دوتایی) از روی فهرست حدود ۷۷۰۰ ترکیب.
    /// مثال: «نرم افزار» ← «نرم‌افزار»، «مخلوط کن» ← «مخلوط‌کن».
    /// </summary>
    private static string ApplyCompounds(string value, SeoKnowledgeBase knowledge, WritingResult result)
    {
        if (knowledge.Compounds.Count == 0)
            return value;
        var words = PersianWord().Matches(value).ToList();
        if (words.Count < 2)
            return value;
        var builder = new StringBuilder(value.Length);
        var cursor = 0;
        var applied = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < words.Count; i++)
        {
            KnowledgeRule? hit = null;
            var span = 0;
            for (var n = 3; n >= 2 && hit is null; n--)
            {
                if (i + n > words.Count || !OnlySpacesBetween(value, words, i, n))
                    continue;
                var key = string.Join(' ', words.Skip(i).Take(n).Select(w => w.Value));
                if (knowledge.Compounds.TryGetValue(key, out var rule) && rule.IsAuto && !string.IsNullOrEmpty(rule.Value))
                {
                    hit = rule;
                    span = n;
                }
            }
            if (hit is null)
                continue;
            var start = words[i].Index;
            var end = words[i + span - 1].Index + words[i + span - 1].Length;
            builder.Append(value, cursor, start - cursor).Append(hit.Value);
            cursor = end;
            if (applied.Add(hit.Term))
                result.Changes.Add(new WritingChange(hit.Id, hit.Term, hit.Value!, "نیم‌فاصله"));
            i += span - 1;
        }
        builder.Append(value, cursor, value.Length - cursor);
        return builder.ToString();
    }

    private static bool OnlySpacesBetween(string value, List<Match> words, int start, int count)
    {
        for (var k = start; k < start + count - 1; k++)
        {
            var gapStart = words[k].Index + words[k].Length;
            var gap = value.AsSpan(gapStart, words[k + 1].Index - gapStart);
            if (gap.Length != 1 || gap[0] != ' ')
                return false;
        }
        return true;
    }

    // ── نیم‌فاصله ──────────────────────────────────────────────────────

    private static readonly string[] PluralSuffixes = ["هایمان", "هایتان", "هایشان", "هایی", "هایم", "هایت", "هایش", "های", "ها"];

    /// <summary>
    /// قاعده‌های نیم‌فاصله:
    /// «می خواهم/میخواهم» ← «می‌خواهم»، «کتاب ها/کتابها» ← «کتاب‌ها»، «بزرگ تر/بزرگترین» ← «بزرگ‌تر/بزرگ‌ترین»،
    /// «خانه ای» ← «خانه‌ای»، «خانه ی» ← «خانهٔ». شکل‌های پرکاربرد چسبیده مثل «بیشتر»، «بهترین» و «تنها» دست نمی‌خورند.
    /// </summary>
    private static string ApplyZwnj(string value, SeoKnowledgeBase knowledge, WritingResult result)
    {
        var before = value;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        void Count(string id) => counts[id] = counts.GetValueOrDefault(id) + 1;

        // «می خواهم» (جدا) ← «می‌خواهم»
        value = SpacedMi().Replace(value, m =>
        {
            var verb = m.Groups[2].Value;
            if (knowledge.Frequency(m.Groups[1].Value + verb) == 0 && verb.Length >= 2)
            {
                Count("b:zwnj-mi");
                return m.Groups[1].Value + PersianText.Zwnj + verb;
            }
            return m.Value;
        });

        value = PersianWord().Replace(value, m =>
        {
            var word = m.Value;
            // «میخواهم» (چسبیده) ← «می‌خواهم»: باقی‌مانده باید فعل باشد و کل واژه یک واژهٔ واقعی مثل «میز» یا «میوه» نباشد.
            foreach (var prefix in new[] { "نمی", "می" })
            {
                if (word.Length - prefix.Length < 2 || !word.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                var rest = word[prefix.Length..];
                if (knowledge.Frequency(word) < 3 && !knowledge.IsInLexicon(word) && knowledge.IsVerbForm(rest))
                {
                    Count("b:zwnj-mi");
                    return prefix + PersianText.Zwnj + rest;
                }
                break;
            }
            // «کتابها» ← «کتاب‌ها»
            foreach (var suffix in PluralSuffixes)
            {
                if (word.Length - suffix.Length < 2 || !word.EndsWith(suffix, StringComparison.Ordinal))
                    continue;
                var stem = word[..^suffix.Length];
                if (knowledge.Frequency(stem) >= 3.5 && knowledge.Frequency(word) < 2)
                {
                    Count("b:zwnj-ha");
                    return stem + PersianText.Zwnj + suffix;
                }
                break;
            }
            // «بزرگترین» ← «بزرگ‌ترین»
            foreach (var suffix in new[] { "ترین", "تر" })
            {
                if (word.Length - suffix.Length < 3 || !word.EndsWith(suffix, StringComparison.Ordinal))
                    continue;
                var stem = word[..^suffix.Length];
                if (knowledge.Frequency(stem) >= 4 && knowledge.Frequency(word) < 1 && !CommonAttached.Contains(word))
                {
                    Count("b:zwnj-tar");
                    return stem + PersianText.Zwnj + suffix;
                }
                break;
            }
            return word;
        });

        // «کتاب ها» ← «کتاب‌ها» (پسوند جدا نوشته‌شده تقریباً همیشه پسوند جمع است)
        value = SpacedPlural().Replace(value, m =>
        {
            Count("b:zwnj-ha");
            return m.Groups[1].Value + PersianText.Zwnj + m.Groups[2].Value;
        });
        // «بزرگ تر» ← «بزرگ‌تر»
        value = SpacedTar().Replace(value, m =>
        {
            if (knowledge.Frequency(m.Groups[1].Value) < 4)
                return m.Value;
            Count("b:zwnj-tar");
            return m.Groups[1].Value + PersianText.Zwnj + m.Groups[2].Value;
        });
        // «خانه ای» ← «خانه‌ای»؛ «خانه ی» ← «خانهٔ»
        value = HeAi().Replace(value, m =>
        {
            Count("b:zwnj-ai");
            return m.Groups[1].Value + PersianText.Zwnj + "ای";
        });
        value = HeYe().Replace(value, m =>
        {
            Count("b:ezafe");
            return m.Groups[1].Value + "ٔ";
        });

        if (!ReferenceEquals(before, value) && before != value)
            foreach (var (id, count) in counts)
                result.Changes.Add(new WritingChange(id, "", PersianText.Digits(count), "نیم‌فاصله"));
        return value;
    }

    /// <summary>شکل‌های چسبیدهٔ رایج که استاندارد نوشتاری پذیرفته‌اند و جدا نمی‌شوند.</summary>
    private static readonly HashSet<string> CommonAttached = new(StringComparer.Ordinal)
    {
        "بیشتر", "بیشترین", "کمتر", "کمترین", "بهتر", "بهترین", "مهمتر", "مهمترین", "بزرگتر", "بزرگترین",
    };

    // ── تکرار، نشانه‌گذاری، رقم ────────────────────────────────────────

    /// <summary>تکرارهای عمدی که معنا دارند و حذف نمی‌شوند.</summary>
    private static readonly HashSet<string> Reduplications = new(StringComparer.Ordinal)
    {
        "کم", "آرام", "تک", "رنگ", "جور", "گاه", "دسته", "گروه", "قدم", "ذره", "لحظه", "بارها", "دانه", "تکه", "خرد", "پاره", "نم", "چک", "به",
    };

    /// <summary>حذف واژهٔ تکراری پشت سر هم («از از» ← «از»). تکرارهای معنادار مثل «کم کم» می‌مانند.</summary>
    private static string RemoveRepeatedWords(string value, WritingResult result)
    {
        var removed = 0;
        value = RepeatedWord().Replace(value, m =>
        {
            var word = m.Groups[1].Value;
            if (Reduplications.Contains(word) || word.All(char.IsDigit))
                return m.Value;
            removed++;
            return word;
        });
        if (removed > 0)
            result.Changes.Add(new WritingChange("b:repeat", "", PersianText.Digits(removed), "تکرار"));
        return value;
    }

    /// <summary>
    /// نشانه‌گذاری فارسی: «,» ← «،»، «;» ← «؛»، «?» ← «؟» (کنار حرف فارسی)، بدون فاصله پیش از نشانه و با یک فاصله بعد از آن،
    /// گیومهٔ فارسی برای "متن فارسی"، و رقم فارسی برای عددهای مستقل («18 ماه» ← «۱۸ ماه»؛ «A55» دست نمی‌خورد).
    /// </summary>
    private static string ApplyPunctuation(string value, WritingResult result)
    {
        var original = value;
        value = PersianComma().Replace(value, m => m.Groups[1].Value + "،");
        value = PersianSemicolon().Replace(value, m => m.Groups[1].Value + "؛");
        value = PersianQuestion().Replace(value, m => m.Groups[1].Value + "؟");
        value = SpaceBeforePunctuation().Replace(value, "$1");
        value = MissingSpaceAfter().Replace(value, "$1 $2");
        value = SpaceInsideOpen().Replace(value, "$1");
        value = SpaceInsideClose().Replace(value, "$1");
        value = StraightQuotes().Replace(value, m => "«" + m.Groups[1].Value.Trim() + "»");
        var current = value;
        value = StandaloneDigits().Replace(value, m =>
            LatinNeighbour(current, m.Index, -1) || LatinNeighbour(current, m.Index + m.Length - 1, +1)
                ? m.Value
                : new string(m.Value.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()));
        if (value != original)
            result.Changes.Add(new WritingChange("b:punctuation", "", "", "نشانه‌گذاری"));
        return value;
    }

    /// <summary>نزدیک‌ترین نویسهٔ غیرفاصله در یک جهت حرف لاتین است؟ («iPhone 15 Pro» → عدد لاتین می‌ماند).</summary>
    private static bool LatinNeighbour(string value, int index, int direction)
    {
        for (var i = index + direction; i >= 0 && i < value.Length; i += direction)
        {
            if (value[i] == ' ')
                continue;
            return char.IsAsciiLetter(value[i]);
        }
        return false;
    }

    /// <summary>کلاس حروف فارسی برای Regex (بدون رقم، نشانه و نیم‌فاصله).</summary>
    private const string L = @"\u0621-\u063A\u0641-\u064A\u067E\u0686\u0698\u06A9\u06AF\u06CC\u0622\u06C0\u0654";

    private static string Tidy(string value)
    {
        value = MultiSpace().Replace(value, " ");
        value = SpaceBeforePunctuation().Replace(value, "$1");
        value = DoubledPunctuation().Replace(value, "$1");
        var lines = value.Split('\n').Select(l => l.Trim().TrimStart('،', '؛', ',')).ToList();
        return string.Join('\n', lines).Trim();
    }

    /// <summary>
    /// واژه‌های ناشناخته با پیشنهاد املایی پربسامد (فقط یادداشت؛ حداکثر ۶ مورد). نام‌های خاص سایت (واژگان) نادیده گرفته می‌شوند.
    /// </summary>
    /// <summary>یادداشت‌های املایی یک متن کامل (برای HTML که گره‌به‌گره ویرایش می‌شود و املا یک بار روی کل متن بررسی می‌شود).</summary>
    public static List<string> SpellingNotes(string text, SeoKnowledgeBase knowledge, IReadOnlySet<string>? vocabulary)
    {
        var result = new WritingResult();
        AddSpellingNotes(text, knowledge, vocabulary, result);
        return result.Notes;
    }

    private static void AddSpellingNotes(string value, SeoKnowledgeBase knowledge, IReadOnlySet<string>? vocabulary, WritingResult result)
    {
        var vocab = vocabulary is null ? null : new HashSet<string>(vocabulary.Select(PersianText.Normalize), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in PersianWord().Matches(value))
        {
            if (seen.Count >= 40 || result.Notes.Count(n => n.StartsWith("املای", StringComparison.Ordinal)) >= 6)
                break;
            var word = match.Value;
            if (!seen.Add(word))
                continue;
            var suggestion = knowledge.SuggestSpelling(word, vocab);
            if (suggestion is not null)
                result.Notes.Add($"املای «{word}» را بررسی کنید؛ شاید «{suggestion}» درست باشد.");
        }
    }

    [GeneratedRegex(@"https?://\S+|www\.\S+|[\w.+-]+@[\w-]+\.[\w.]+", RegexOptions.IgnoreCase)]
    private static partial Regex Protected();

    /// <summary>یک واژهٔ فارسی (حروف فارسی بدون نیم‌فاصله).</summary>
    [GeneratedRegex("[" + L + "]+")]
    private static partial Regex PersianWord();

    [GeneratedRegex(@"(?<![\p{L}\u200C])(ن?می) ([" + L + "]{2,})")]
    private static partial Regex SpacedMi();

    [GeneratedRegex("([" + L + "]{2,}) (هایمان|هایتان|هایشان|هایی|هایم|هایت|هایش|های|ها)" + @"(?![\p{L}\u200C])")]
    private static partial Regex SpacedPlural();

    [GeneratedRegex("([" + L + "]{2,}) (ترین|تر)" + @"(?![\p{L}\u200C])")]
    private static partial Regex SpacedTar();

    [GeneratedRegex("([" + L + "]+ه) ای" + @"(?![\p{L}\u200C])")]
    private static partial Regex HeAi();

    [GeneratedRegex("([" + L + "]+ه) ی(?= )")]
    private static partial Regex HeYe();

    [GeneratedRegex(@"(?<![\p{L}\p{N}\u200C])([\p{L}\u200C]{2,}) \1(?![\p{L}\p{N}\u200C])")]
    private static partial Regex RepeatedWord();

    [GeneratedRegex(@"([\u0600-\u06FF»)])\s*,(?!\d)")]
    private static partial Regex PersianComma();

    [GeneratedRegex(@"([\u0600-\u06FF»)])\s*;")]
    private static partial Regex PersianSemicolon();

    [GeneratedRegex(@"([\u0600-\u06FF»)])\s*\?")]
    private static partial Regex PersianQuestion();

    [GeneratedRegex(@"[ \t]+([،؛؟!:.»)\]])")]
    private static partial Regex SpaceBeforePunctuation();

    [GeneratedRegex(@"([،؛؟!])([\u0600-\u06FF«(])")]
    private static partial Regex MissingSpaceAfter();

    [GeneratedRegex(@"([«(\[])[ \t]+")]
    private static partial Regex SpaceInsideOpen();

    [GeneratedRegex(@"[ \t]+(?=[»)\]])")]
    private static partial Regex SpaceInsideClose();

    [GeneratedRegex(@"""([^""\n]*[\u0600-\u06FF][^""\n]*)""")]
    private static partial Regex StraightQuotes();

    /// <summary>عدد مستقل (نه چسبیده به حرف لاتین، نه داخل مدل/نسخه مثل A55 یا v2.1 یا 1000XM5).</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9_\-.#/:])[0-9]+(?:[.,:/][0-9]+)*(?![A-Za-z0-9_\-#])")]
    private static partial Regex StandaloneDigits();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultiSpace();

    [GeneratedRegex(@"([،؛])\s*[،؛,]")]
    private static partial Regex DoubledPunctuation();
}
