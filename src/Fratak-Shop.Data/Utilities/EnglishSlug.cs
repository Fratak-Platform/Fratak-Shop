using System.Globalization;
using System.Text;

namespace Fratak_Shop.Data.Utilities;

/// <summary>
/// سازندهٔ «نامک انگلیسی» (Slug) – دور سئوی خودکار.
/// <para>چرا؟ آدرس فارسی در گوگل کار می‌کند ولی هنگام اشتراک‌گذاری به رشته‌ای طولانی و ناخوانا تبدیل می‌شود
/// (مثلاً <c>/product/%DA%AF%D9%88%D8%B4%DB%8C</c>) و در بعضی ابزارها می‌شکند. صاحب فروشگاه خواسته همهٔ نامک‌ها انگلیسی باشند.</para>
/// <para>روش (بدون هیچ سرویس بیرونی):</para>
/// <list type="number">
/// <item>ترجمهٔ واژه‌ها و عبارت‌های رایج فروشگاهی با واژه‌نامهٔ داخلی (طولانی‌ترین عبارت اول): «گوشی موبایل» ← mobile-phone،
/// «بی سیم» ← wireless، «سامسونگ» ← samsung.</item>
/// <item>واژه‌های بی‌معادل (اسم خاص) آوانویسی می‌شوند: «فراتک» ← fratak، «علی» ← ali.</item>
/// <item>واژه‌های پرکننده («و»، «مدل»، «با»، «برای»...) حذف می‌شوند تا آدرس کوتاه بماند.</item>
/// <item>صفت‌های انگلیسی پیش از اسم می‌آیند: «شارژر سریع» ← fast-charger.</item>
/// </list>
/// خروجی فقط a-z، 0-9 و خط تیرهٔ تکی است و همیشه پایدار است: <c>From(From(x)) == From(x)</c>.
/// <para>محل استفاده: قانون ذخیرهٔ <see cref="Contexts.FratakShopDbContext"/> (هر نامکی از هر مسیری – پنل، درون‌ریزی، همگام‌سازی
/// تأمین‌کننده، کپی محصول – پیش از ذخیره انگلیسی می‌شود)، <c>SlugHelper.ToSlug</c> در Core و کار یک‌بارهٔ تبدیل نامک‌های قدیمی.</para>
/// </summary>
/// <example>
/// <code>
/// EnglishSlug.From("گوشی موبایل سامسونگ مدل Galaxy A55")  // "mobile-phone-samsung-galaxy-a55"
/// EnglishSlug.From("هدفون بی‌سیم با میکروفون")             // "wireless-headphones-microphone"
/// EnglishSlug.From("درباره ما")                             // "about-us"
/// EnglishSlug.IsEnglish("galaxy-a55")                       // true
/// </code>
/// </example>
public static class EnglishSlug
{
    /// <summary>سقف پیش‌فرض طول نامک تازه (آدرس کوتاه‌تر در نتایج گوگل کامل دیده می‌شود).</summary>
    public const int DefaultMaxLength = 90;

    /// <summary>
    /// آیا نامک از قبل انگلیسیِ تمیز است؟ (فقط a-z، 0-9 و خط تیرهٔ تکی، بدون خط تیره در ابتدا/انتها)
    /// مثال: <c>IsEnglish("samsung-a55") == true</c>، <c>IsEnglish("گوشی") == false</c>، <c>IsEnglish("A55") == false</c>.
    /// </summary>
    public static bool IsEnglish(string? slug)
    {
        if (string.IsNullOrEmpty(slug) || slug[0] == '-' || slug[^1] == '-')
            return false;
        var previousDash = false;
        foreach (var c in slug)
        {
            if (c == '-')
            {
                if (previousDash)
                    return false;
                previousDash = true;
                continue;
            }
            previousDash = false;
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9')))
                return false;
        }
        return true;
    }

    /// <summary>
    /// ساخت نامک انگلیسی از هر متنی (فارسی، انگلیسی یا ترکیبی). متن خالی ← رشتهٔ خالی.
    /// <para>برش در مرز واژه انجام می‌شود تا واژهٔ نیمه‌کاره در آدرس نماند (مگر یک واژهٔ بلندتر از سقف).</para>
    /// </summary>
    public static string From(string? text, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || maxLength <= 0)
            return string.Empty;

        var words = Tokenize(text);
        var output = new List<string>(words.Count);
        for (var i = 0; i < words.Count;)
        {
            // طولانی‌ترین عبارت شناخته‌شده (تا ۴ واژه) اول؛ «تماس با ما» نباید «contact» + حذف «با» + «us» شود.
            var matched = false;
            for (var span = Math.Min(4, words.Count - i); span >= 2; span--)
            {
                var phrase = string.Join(' ', words.Skip(i).Take(span));
                if (Lexicon.TryGetValue(phrase, out var english))
                {
                    output.Add(english);
                    i += span;
                    matched = true;
                    break;
                }
            }
            if (matched)
                continue;

            var word = words[i++];
            var translated = TranslateWord(word);
            if (translated.Length > 0)
                output.Add(translated);
        }

        ReorderAdjectives(output);
        return Join(output, maxLength);
    }

    /// <summary>
    /// نامک انگلیسی با اولویت متن‌های جایگزین: اول اولین متنی که حرف لاتین دارد (مثلاً نام انگلیسی محصول)،
    /// بعد خود متن اصلی. مثال: <c>Prefer("گوشی سامسونگ", "Samsung Galaxy A55")</c> ← <c>"samsung-galaxy-a55"</c>.
    /// </summary>
    public static string Prefer(string? text, params string?[] latinCandidates)
    {
        foreach (var candidate in latinCandidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && candidate.Any(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z')))
            {
                var slug = From(candidate);
                if (slug.Length >= 3)
                    return slug;
            }
        }
        return From(text);
    }

    /// <summary>
    /// نامک یکتا: اگر <paramref name="taken"/> نامک را داشت، «-2»، «-3» ... اضافه می‌شود (سقف طول رعایت می‌شود).
    /// مثال: <c>Unique("case", s => s == "case")</c> ← <c>"case-2"</c>.
    /// </summary>
    public static string Unique(string slug, Func<string, bool> taken, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(slug))
            slug = "item";
        if (!taken(slug))
            return slug;
        for (var n = 2; n < 10_000; n++)
        {
            var suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
            var head = slug.Length + suffix.Length > maxLength ? slug[..Math.Max(1, maxLength - suffix.Length)].TrimEnd('-') : slug;
            var candidate = head + suffix;
            if (!taken(candidate))
                return candidate;
        }
        return slug + "-" + Guid.NewGuid().ToString("N")[..8];
    }

    // ── مراحل داخلی ───────────────────────────────────────────────────────

    /// <summary>
    /// شکستن متن به واژه‌ها با یکسان‌سازی: ي/ك عربی، ارقام فارسی/عربی، نیم‌فاصله (داخل واژه می‌ماند و حذف می‌شود)،
    /// حروف لاتین کوچک و بدون اعراب (é ← e). مرز «حرف لاتین/رقم ↔ حرف فارسی» هم واژه را جدا می‌کند («A55گوشی»).
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        var currentScript = 0; // 1 = لاتین/رقم، 2 = فارسی

        void Flush()
        {
            if (current.Length > 0)
                words.Add(current.ToString());
            current.Clear();
            currentScript = 0;
        }

        foreach (var raw in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark)
                continue; // اعراب لاتین و فارسی (فتحه، کسره، تشدید)
            var c = NormalizeChar(raw);
            if (c == '\u200C' || c == '\u200D' || c == 'ـ')
                continue; // نیم‌فاصله و کشیده داخل واژه: «می‌شود» یک واژه است

            var script = c switch
            {
                >= 'a' and <= 'z' or >= '0' and <= '9' => 1,
                _ when IsPersianLetter(c) => 2,
                _ => 0,
            };
            if (script == 0)
            {
                Flush();
                continue;
            }
            if (currentScript != 0 && script != currentScript)
                Flush();
            currentScript = script;
            current.Append(c);
        }
        Flush();
        return words;
    }

    private static char NormalizeChar(char c) => c switch
    {
        >= 'A' and <= 'Z' => char.ToLowerInvariant(c),
        'ي' or 'ى' or 'ئ' => 'ی',
        'ك' => 'ک',
        'ة' => 'ه',
        'أ' or 'إ' or 'ٱ' => 'ا',
        'ؤ' => 'و',
        >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
        >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
        'ı' => 'i',
        'ß' => 's',
        'ø' => 'o',
        'æ' => 'a',
        'đ' => 'd',
        'ł' => 'l',
        _ => c,
    };

    private static bool IsPersianLetter(char c) => c is 'آ' or 'ء' || Letters.ContainsKey(c);

    /// <summary>ترجمه یا آوانویسی یک واژه؛ واژهٔ پرکننده ← رشتهٔ خالی.</summary>
    private static string TranslateWord(string word)
    {
        if (word[0] is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            return word; // لاتین/رقم همان‌طور (حروف کوچک شده‌اند)

        if (StopWords.Contains(word))
            return string.Empty;
        if (Lexicon.TryGetValue(word, out var english))
            return english;

        // جمع و پسوندها: «هدفون‌ها» ← headphones، «گوشی‌های» ← phones، «کتابی» نه (یای نکره در نامک مهم نیست).
        foreach (var (suffix, plural) in Suffixes)
        {
            if (word.Length > suffix.Length + 1 && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                var stem = word[..^suffix.Length];
                if (Lexicon.TryGetValue(stem, out var stemEnglish))
                    return plural && !stemEnglish.EndsWith('s') && !stemEnglish.Contains('-') ? stemEnglish + "s" : stemEnglish;
            }
        }

        return Transliterate(word);
    }

    private static readonly (string Suffix, bool Plural)[] Suffixes =
    [
        ("هایی", true), ("های", true), ("ها", true), ("ان", true), ("ترین", false), ("تر", false), ("ی", false),
    ];

    /// <summary>
    /// آوانویسی فارسی به لاتین (روش رایج «فینگلیش» تمیز). حروف صدادار کوتاه در خط فارسی نوشته نمی‌شوند؛ دو قاعدهٔ ساده
    /// خوانایی را بهتر می‌کند: واژهٔ بی‌صدا یک «a» پس از اولین حرف می‌گیرد («سرد» ← sard) و دو بی‌صدای پایانی با یک «a»
    /// جدا می‌شوند وقتی واژه فقط یک صدادار دارد («فراتک» ← fratak).
    /// </summary>
    public static string Transliterate(string word)
    {
        var builder = new StringBuilder(word.Length * 2);
        for (var i = 0; i < word.Length; i++)
        {
            var c = word[i];
            var previous = i > 0 ? word[i - 1] : '\0';
            var next = i + 1 < word.Length ? word[i + 1] : '\0';
            var last = i == word.Length - 1;
            switch (c)
            {
                case 'آ':
                case 'ا':
                    builder.Append('a');
                    break;
                case 'و':
                    if (i == 0 || previous is 'ا' or 'آ' or 'و' || next is 'ا' or 'ی')
                        builder.Append('v');
                    else
                        builder.Append('o');
                    break;
                case 'ی':
                    if (i == 0 || next is 'ا' or 'و' or 'آ' || (previous is 'ا' or 'آ' or 'و' && !last))
                        builder.Append('y');
                    else
                        builder.Append('i');
                    break;
                case 'ه':
                    builder.Append(last && i > 0 ? "e" : "h");
                    break;
                case 'ع':
                    if (i == 0 || !(previous is 'ا' or 'و' or 'ی' or 'آ'))
                        builder.Append('a');
                    break;
                case 'ء':
                    break;
                default:
                    if (Letters.TryGetValue(c, out var latin))
                        builder.Append(latin);
                    break;
            }
        }

        var result = builder.ToString();
        if (result.Length == 0)
            return result;

        var vowels = result.Count(ch => ch is 'a' or 'e' or 'i' or 'o' or 'u');
        if (vowels == 0 && result.Length >= 2)
        {
            var firstConsonant = ConsonantLength(result, 0);
            return result[..firstConsonant] + "a" + result[firstConsonant..];
        }

        if (vowels == 1 && result.Length >= 4 && !IsVowel(result[^1]) && !IsVowel(result[^2]) && !IsDigraph(result[^2..]))
            return result[..^1] + "a" + result[^1];

        return result;
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    private static bool IsDigraph(string pair) => pair is "kh" or "sh" or "ch" or "zh" or "gh";

    private static int ConsonantLength(string text, int at) =>
        at + 1 < text.Length && IsDigraph(text.Substring(at, 2)) ? 2 : 1;

    /// <summary>صفت‌هایی که در فارسی پس از اسم و در انگلیسی پیش از آن می‌آیند: «شارژر سریع» ← fast-charger.</summary>
    private static void ReorderAdjectives(List<string> words)
    {
        for (var i = 1; i < words.Count; i++)
        {
            if (Adjectives.Contains(words[i]) && !Adjectives.Contains(words[i - 1]) && Nouns.Contains(words[i - 1]))
            {
                (words[i - 1], words[i]) = (words[i], words[i - 1]);
                i++;
            }
        }
    }

    private static string Join(List<string> words, int maxLength)
    {
        var builder = new StringBuilder();
        string? previous = null;
        foreach (var word in words)
        {
            // ترجمهٔ تکراری پشت سر هم («گوشی موبایل» + «موبایل») حذف می‌شود.
            if (previous is not null && string.Equals(previous, word, StringComparison.Ordinal))
                continue;
            var clean = CleanAscii(word);
            if (clean.Length == 0)
                continue;
            var extra = (builder.Length > 0 ? 1 : 0) + clean.Length;
            if (builder.Length + extra > maxLength)
            {
                if (builder.Length == 0)
                    builder.Append(clean[..maxLength].Trim('-'));
                break;
            }
            if (builder.Length > 0)
                builder.Append('-');
            builder.Append(clean);
            previous = word;
        }
        return builder.ToString().Trim('-');
    }

    /// <summary>فقط a-z و 0-9 و خط تیرهٔ تکی (از داخل عبارت‌های چندواژه‌ای واژه‌نامه).</summary>
    private static string CleanAscii(string word)
    {
        var builder = new StringBuilder(word.Length);
        var dash = false;
        foreach (var c in word)
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(c);
                dash = false;
            }
            else if (!dash && builder.Length > 0)
            {
                builder.Append('-');
                dash = true;
            }
        }
        return builder.ToString().Trim('-');
    }

    // ── داده‌ها ──────────────────────────────────────────────────────────

    private static readonly Dictionary<char, string> Letters = new()
    {
        ['ا'] = "a", ['آ'] = "a", ['ب'] = "b", ['پ'] = "p", ['ت'] = "t", ['ث'] = "s", ['ج'] = "j", ['چ'] = "ch",
        ['ح'] = "h", ['خ'] = "kh", ['د'] = "d", ['ذ'] = "z", ['ر'] = "r", ['ز'] = "z", ['ژ'] = "zh", ['س'] = "s",
        ['ش'] = "sh", ['ص'] = "s", ['ض'] = "z", ['ط'] = "t", ['ظ'] = "z", ['ع'] = "a", ['غ'] = "gh", ['ف'] = "f",
        ['ق'] = "gh", ['ک'] = "k", ['گ'] = "g", ['ل'] = "l", ['م'] = "m", ['ن'] = "n", ['و'] = "v", ['ه'] = "h",
        ['ی'] = "y",
    };

    /// <summary>واژه‌های پرکننده که در نامک معنایی اضافه نمی‌کنند.</summary>
    private static readonly HashSet<string> StopWords = new(NormalizeKeys(new[]
    {
        "و", "با", "در", "به", "از", "که", "را", "برای", "این", "آن", "یک", "مدل", "سری", "ای", "هم", "تا", "یا", "بر", "های",
        "ها", "است", "شده", "می", "کد", "نوع", "جهت", "همراه", "دارای", "مخصوص", "ویژه", "طرح", "عدد", "عددی", "بسته", "تایی",
    }), StringComparer.Ordinal);

    /// <summary>عبارت‌های چندواژه‌ای (کلید با فاصلهٔ تکی و بدون نیم‌فاصله).</summary>
    private static readonly Dictionary<string, string> Phrases = new(StringComparer.Ordinal)
    {
        ["گوشی موبایل"] = "mobile-phone", ["تلفن همراه"] = "mobile-phone", ["گوشی هوشمند"] = "smartphone",
        ["لپ تاپ"] = "laptop", ["نوت بوک"] = "notebook", ["بی سیم"] = "wireless", ["ساعت هوشمند"] = "smartwatch",
        ["مچ بند هوشمند"] = "smart-band", ["دستبند هوشمند"] = "smart-band", ["پاور بانک"] = "power-bank",
        ["شارژر همراه"] = "power-bank", ["هارد اکسترنال"] = "external-hard-drive", ["هارد اینترنال"] = "internal-hard-drive",
        ["کارت حافظه"] = "memory-card", ["فلش مموری"] = "flash-drive", ["محافظ صفحه"] = "screen-protector",
        ["گلس محافظ"] = "screen-protector", ["محافظ لنز"] = "lens-protector", ["شارژر دیواری"] = "wall-charger",
        ["شارژر فندکی"] = "car-charger", ["شارژر ماشین"] = "car-charger", ["شارژر بی سیم"] = "wireless-charger",
        ["هدفون بی سیم"] = "wireless-headphones", ["هندزفری بی سیم"] = "wireless-earbuds", ["هدست بی سیم"] = "wireless-headset",
        ["اسپیکر بلوتوثی"] = "bluetooth-speaker", ["اسپیکر بلوتوث"] = "bluetooth-speaker", ["هندزفری بلوتوث"] = "bluetooth-earbuds",
        ["هندزفری بلوتوثی"] = "bluetooth-earbuds", ["کابل شارژ"] = "charging-cable", ["کابل تبدیل"] = "adapter-cable",
        ["هاب یو اس بی"] = "usb-hub", ["یو اس بی"] = "usb", ["تایپ سی"] = "type-c", ["لایتنینگ"] = "lightning",
        ["میکرو یو اس بی"] = "micro-usb", ["قاب گوشی"] = "phone-case", ["کیف لپ تاپ"] = "laptop-bag", ["پایه نگهدارنده"] = "holder",
        ["هولدر موبایل"] = "phone-holder", ["کنسول بازی"] = "game-console", ["دسته بازی"] = "gamepad", ["ماوس پد"] = "mouse-pad",
        ["کیبورد گیمینگ"] = "gaming-keyboard", ["ماوس گیمینگ"] = "gaming-mouse", ["تلویزیون هوشمند"] = "smart-tv",
        ["دوربین مداربسته"] = "cctv-camera", ["دوربین عکاسی"] = "camera", ["ریش تراش"] = "shaver", ["ماشین اصلاح"] = "trimmer",
        ["سشوار"] = "hair-dryer", ["اتو مو"] = "hair-straightener", ["جاروبرقی"] = "vacuum-cleaner", ["جارو برقی"] = "vacuum-cleaner",
        ["چای ساز"] = "tea-maker", ["قهوه ساز"] = "coffee-maker", ["آب میوه گیری"] = "juicer", ["مودم روتر"] = "modem-router",
        ["کارت گرافیک"] = "graphics-card", ["مادربرد"] = "motherboard", ["منبع تغذیه"] = "power-supply", ["حافظه رم"] = "ram",
        ["حافظه داخلی"] = "storage", ["اس اس دی"] = "ssd", ["ال جی"] = "lg", ["اچ پی"] = "hp", ["جی بی ال"] = "jbl",
        ["تی سی ال"] = "tcl", ["کیو سی وای"] = "qcy", ["وان پلاس"] = "oneplus", ["ریلمی"] = "realme", ["ردمی"] = "redmi",
        ["گوگل پیکسل"] = "google-pixel", ["گلکسی"] = "galaxy", ["آیفون"] = "iphone", ["آیپد"] = "ipad", ["ایرپاد"] = "airpods",
        ["مک بوک"] = "macbook", ["اپل واچ"] = "apple-watch", ["پلی استیشن"] = "playstation", ["ایکس باکس"] = "xbox",
        ["درباره ما"] = "about-us", ["تماس با ما"] = "contact-us", ["قوانین و مقررات"] = "terms-and-conditions",
        ["شرایط استفاده"] = "terms-of-use", ["حریم خصوصی"] = "privacy-policy", ["سوالات متداول"] = "faq", ["سؤالات متداول"] = "faq",
        ["پرسش های متداول"] = "faq", ["پرسشهای متداول"] = "faq", ["شیوه ارسال"] = "shipping", ["روش ارسال"] = "shipping",
        ["شیوه های ارسال"] = "shipping-methods", ["روش های ارسال"] = "shipping-methods", ["رویه بازگشت کالا"] = "return-policy",
        ["بازگشت کالا"] = "returns", ["مرجوعی کالا"] = "returns", ["شرایط مرجوعی"] = "return-policy", ["راهنمای خرید"] = "buying-guide",
        ["شیوه پرداخت"] = "payment-methods", ["روش های پرداخت"] = "payment-methods", ["پیگیری سفارش"] = "order-tracking",
        ["همکاری با ما"] = "careers", ["فرصت های شغلی"] = "careers", ["نقشه سایت"] = "sitemap", ["ضمانت اصالت"] = "authenticity-guarantee",
        ["گارانتی اصالت"] = "authenticity-guarantee", ["ارسال رایگان"] = "free-shipping", ["پرداخت در محل"] = "cash-on-delivery",
        ["مقایسه قیمت"] = "price-comparison", ["بهترین قیمت"] = "best-price", ["قیمت روز"] = "price-today", ["نقره ای"] = "silver",
        ["سرمه ای"] = "navy", ["قهوه ای"] = "brown", ["طوسی"] = "gray", ["فیروزه ای"] = "turquoise", ["شارژ سریع"] = "fast-charging",
        ["نویز کنسلینگ"] = "noise-cancelling", ["حذف نویز"] = "noise-cancelling", ["ضد آب"] = "waterproof", ["ضد ضربه"] = "shockproof",
        ["دو سیم کارت"] = "dual-sim", ["تک سیم کارت"] = "single-sim", ["گیگ"] = "gb", ["مگاپیکسل"] = "mp", ["میلی آمپر ساعت"] = "mah",
        ["میلی آمپر"] = "mah", ["اینچ"] = "inch", ["یک ترابایت"] = "1tb", ["دو ترابایت"] = "2tb",
    };

    /// <summary>واژه‌نامهٔ تک‌واژه‌ای فروشگاهی (حروف کوچک، بدون نیم‌فاصله).</summary>
    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        // دستگاه‌ها و لوازم
        ["گوشی"] = "phone", ["موبایل"] = "mobile", ["تلفن"] = "phone", ["تبلت"] = "tablet", ["لپتاپ"] = "laptop",
        ["نوتبوک"] = "notebook", ["کامپیوتر"] = "computer", ["رایانه"] = "computer", ["مانیتور"] = "monitor", ["کیبورد"] = "keyboard",
        ["صفحه کلید"] = "keyboard", ["ماوس"] = "mouse", ["موس"] = "mouse", ["هدفون"] = "headphones", ["هندزفری"] = "earbuds",
        ["هدست"] = "headset", ["ایرفون"] = "earphones", ["اسپیکر"] = "speaker", ["بلندگو"] = "speaker", ["میکروفون"] = "microphone",
        ["شارژر"] = "charger", ["آداپتور"] = "adapter", ["آداپتر"] = "adapter", ["کابل"] = "cable", ["سیم"] = "cable",
        ["تبدیل"] = "adapter", ["مبدل"] = "converter", ["هاب"] = "hub", ["پاوربانک"] = "power-bank", ["باتری"] = "battery",
        ["ساعت"] = "watch", ["مچبند"] = "band", ["دستبند"] = "bracelet", ["قاب"] = "case", ["کاور"] = "cover", ["کیف"] = "bag",
        ["کوله"] = "backpack", ["گلس"] = "glass", ["محافظ"] = "protector", ["بند"] = "strap", ["پایه"] = "stand", ["هولدر"] = "holder",
        ["نگهدارنده"] = "holder", ["سه پایه"] = "tripod", ["رینگ"] = "ring", ["لنز"] = "lens", ["دوربین"] = "camera", ["فلش"] = "flash",
        ["رم"] = "ram", ["هارد"] = "hard-drive", ["حافظه"] = "memory", ["مموری"] = "memory", ["مودم"] = "modem", ["روتر"] = "router",
        ["اکسس پوینت"] = "access-point", ["سوئیچ"] = "switch", ["کنسول"] = "console", ["بازی"] = "game",
        ["گیمپد"] = "gamepad", ["پرینتر"] = "printer", ["چاپگر"] = "printer", ["اسکنر"] = "scanner", ["پروژکتور"] = "projector",
        ["تلویزیون"] = "tv", ["گیرنده"] = "receiver", ["ریموت"] = "remote", ["کنترل"] = "remote", ["چراغ"] = "lamp", ["لامپ"] = "bulb",
        ["پنکه"] = "fan", ["فن"] = "fan", ["خنک کننده"] = "cooler", ["کولر"] = "cooler", ["اتو"] = "iron", ["سشوار"] = "hair-dryer",
        ["ریشتراش"] = "shaver", ["اصلاح"] = "trimmer", ["ترازو"] = "scale", ["قلم"] = "pen", ["استایلوس"] = "stylus", ["کارت"] = "card",
        ["سیمکارت"] = "sim-card", ["فیلم"] = "film", ["برچسب"] = "sticker", ["اسکین"] = "skin", ["کیس"] = "case", ["پاور"] = "power",
        ["اسپلیتر"] = "splitter", ["سوکت"] = "socket", ["پریز"] = "outlet", ["چندراهی"] = "power-strip", ["محافظ برق"] = "surge-protector",
        ["ترموس"] = "thermos", ["فلاسک"] = "flask", ["کتاب"] = "book", ["عینک"] = "glasses", ["ربات"] = "robot", ["پهپاد"] = "drone",
        ["کوادکوپتر"] = "quadcopter", ["اسکوتر"] = "scooter", ["دوچرخه"] = "bicycle", ["اسباب بازی"] = "toy",
        // صفت‌ها و ویژگی‌ها
        ["هوشمند"] = "smart", ["سریع"] = "fast", ["فست"] = "fast", ["بیسیم"] = "wireless", ["وایرلس"] = "wireless",
        ["بلوتوث"] = "bluetooth", ["بلوتوثی"] = "bluetooth", ["مغناطیسی"] = "magnetic", ["مگنتی"] = "magnetic", ["مگنتیک"] = "magnetic",
        ["اصل"] = "original", ["اورجینال"] = "original", ["اصلی"] = "original", ["جدید"] = "new", ["قابل حمل"] = "portable",
        ["پرتابل"] = "portable", ["گیمینگ"] = "gaming", ["حرفه ای"] = "pro", ["حرفهای"] = "pro", ["ارزان"] = "cheap", ["مینی"] = "mini",
        ["کوچک"] = "small", ["بزرگ"] = "large", ["چرمی"] = "leather", ["چرم"] = "leather", ["سیلیکونی"] = "silicone", ["فلزی"] = "metal",
        ["پلاستیکی"] = "plastic", ["شفاف"] = "clear", ["ضدآب"] = "waterproof", ["ضدضربه"] = "shockproof", ["رومیزی"] = "desktop",
        ["دیواری"] = "wall", ["فندکی"] = "car", ["ماشین"] = "car", ["خودرو"] = "car", ["دوگانه"] = "dual", ["دوقلو"] = "dual",
        ["تاشو"] = "foldable", ["چرخشی"] = "rotating", ["لمسی"] = "touch", ["دیجیتال"] = "digital", ["آنالوگ"] = "analog",
        ["شارژی"] = "rechargeable", ["برقی"] = "electric", ["ورزشی"] = "sport", ["کودک"] = "kids", ["کودکان"] = "kids",
        ["زنانه"] = "women", ["مردانه"] = "men", ["اسپرت"] = "sport", ["کلاسیک"] = "classic", ["فانتزی"] = "fancy", ["لاکچری"] = "luxury",
        ["ال ای دی"] = "led", ["آر جی بی"] = "rgb", ["نانو"] = "nano", ["پرو"] = "pro", ["مکس"] = "max", ["پلاس"] = "plus",
        ["لایت"] = "lite", ["اولترا"] = "ultra", ["الترا"] = "ultra", ["نئو"] = "neo", ["اج"] = "edge",
        // رنگ‌ها
        ["مشکی"] = "black", ["سیاه"] = "black", ["سفید"] = "white", ["آبی"] = "blue", ["قرمز"] = "red", ["سبز"] = "green",
        ["زرد"] = "yellow", ["نارنجی"] = "orange", ["صورتی"] = "pink", ["بنفش"] = "purple", ["طلایی"] = "gold", ["نقره"] = "silver",
        ["نقرهای"] = "silver", ["خاکستری"] = "gray", ["کرم"] = "cream", ["رزگلد"] = "rose-gold", ["سرمهای"] = "navy", ["قهوهای"] = "brown",
        ["یاسی"] = "lilac", ["زرشکی"] = "maroon", ["بژ"] = "beige", ["رنگی"] = "color", ["رنگ"] = "color",
        // واحدها و اعداد
        ["گیگابایت"] = "gb", ["ترابایت"] = "tb", ["مگابایت"] = "mb", ["وات"] = "w", ["آمپر"] = "a", ["ولت"] = "v", ["متر"] = "m",
        ["سانتیمتر"] = "cm", ["سانتی"] = "cm", ["میلیمتر"] = "mm", ["هرتز"] = "hz", ["پورت"] = "port", ["خروجی"] = "output",
        ["ورودی"] = "input", ["ظرفیت"] = "capacity", ["یک"] = "1", ["دو"] = "2", ["سه"] = "3", ["چهار"] = "4", ["پنج"] = "5",
        // برندها (املای فارسی رایج)
        ["سامسونگ"] = "samsung", ["اپل"] = "apple", ["شیائومی"] = "xiaomi", ["شیاومی"] = "xiaomi", ["شائومی"] = "xiaomi",
        ["هواوی"] = "huawei", ["هوآوی"] = "huawei", ["آنر"] = "honor", ["نوکیا"] = "nokia", ["سونی"] = "sony", ["موتورولا"] = "motorola",
        ["اوپو"] = "oppo", ["ویوو"] = "vivo", ["انکر"] = "anker", ["باسئوس"] = "baseus", ["بیسوس"] = "baseus", ["اوریکو"] = "orico",
        ["یوگرین"] = "ugreen", ["لنوو"] = "lenovo", ["ایسوس"] = "asus", ["ایسر"] = "acer", ["دل"] = "dell", ["مایکروسافت"] = "microsoft",
        ["گوگل"] = "google", ["تسکو"] = "tsco", ["بیاند"] = "beyond", ["هیسکا"] = "hiska", ["رسی"] = "recci", ["جوی روم"] = "joyroom",
        ["جویروم"] = "joyroom", ["هوکو"] = "hoco", ["بروفون"] = "borofone", ["ایکس او"] = "xo", ["ریمکس"] = "remax", ["کینگستون"] = "kingston",
        ["سن دیسک"] = "sandisk", ["سندیسک"] = "sandisk", ["وسترن دیجیتال"] = "western-digital", ["سیگیت"] = "seagate", ["ترنسند"] = "transcend",
        ["ای دیتا"] = "adata", ["تی پی لینک"] = "tp-link", ["دی لینک"] = "d-link", ["لاجیتک"] = "logitech", ["ریزر"] = "razer",
        ["فیلیپس"] = "philips", ["پاناسونیک"] = "panasonic", ["براون"] = "braun", ["بوش"] = "bosch", ["کنوود"] = "kenwood",
        ["کیوسیوای"] = "qcy", ["هایلو"] = "haylou", ["امیزفیت"] = "amazfit", ["گارمین"] = "garmin", ["بیتس"] = "beats",
        ["مارشال"] = "marshall", ["هارمن"] = "harman", ["گرین لاین"] = "green-lion", ["پرودو"] = "porodo", ["نیلکین"] = "nillkin",
        ["اسپیگن"] = "spigen", ["یسیدو"] = "yesido", ["وریتی"] = "verity", ["فراتک"] = "fratak",
        // صفحه‌ها و محتوا
        ["خرید"] = "buy", ["قیمت"] = "price", ["فروش"] = "sale", ["فروشگاه"] = "shop", ["محصول"] = "product", ["محصولات"] = "products",
        ["دسته"] = "category", ["دستهبندی"] = "category", ["برند"] = "brand", ["مقاله"] = "article", ["مقالات"] = "articles",
        ["وبلاگ"] = "blog", ["بلاگ"] = "blog", ["اخبار"] = "news", ["خبر"] = "news", ["راهنما"] = "guide", ["راهنمای"] = "guide",
        ["آموزش"] = "tutorial", ["معرفی"] = "introduction", ["بررسی"] = "review", ["نقد"] = "review", ["مقایسه"] = "comparison",
        ["بهترین"] = "best", ["برترین"] = "top", ["ارزانترین"] = "cheapest", ["چیست"] = "what-is", ["چگونه"] = "how-to",
        ["روش"] = "method", ["روشهای"] = "methods", ["نکات"] = "tips", ["نکته"] = "tip", ["ترفند"] = "tricks", ["ترفندهای"] = "tricks",
        ["مشخصات"] = "specs", ["ویژگی"] = "features", ["ویژگیهای"] = "features", ["تفاوت"] = "difference", ["فرق"] = "difference",
        ["انتخاب"] = "choose", ["نصب"] = "install", ["تعمیر"] = "repair", ["نگهداری"] = "care", ["عیب"] = "problem", ["مشکل"] = "problem",
        ["حل"] = "fix", ["رفع"] = "fix", ["افزایش"] = "increase", ["کاهش"] = "reduce", ["عمر"] = "life", ["سرعت"] = "speed",
        ["کیفیت"] = "quality", ["امنیت"] = "security", ["سلامت"] = "health", ["تخفیف"] = "discount", ["فروش ویژه"] = "special-sale",
        ["جشنواره"] = "festival", ["حراج"] = "sale", ["پیشنهاد"] = "offer", ["هدیه"] = "gift", ["کادو"] = "gift", ["عید"] = "eid",
        ["نوروز"] = "nowruz", ["یلدا"] = "yalda", ["مدرسه"] = "school", ["دانشجو"] = "student", ["دانشجویی"] = "student",
        ["گارانتی"] = "warranty", ["ضمانت"] = "guarantee", ["ارسال"] = "shipping", ["پرداخت"] = "payment", ["سفارش"] = "order",
        ["قوانین"] = "terms", ["مقررات"] = "rules", ["تماس"] = "contact", ["درباره"] = "about", ["ما"] = "us", ["سوالات"] = "questions",
        ["سؤالات"] = "questions", ["متداول"] = "faq", ["حریم"] = "privacy", ["خصوصی"] = "private", ["همکاری"] = "cooperation",
        ["عمده"] = "wholesale", ["همکار"] = "partner", ["نمایندگی"] = "agency", ["شعبه"] = "branch", ["آدرس"] = "address",
        ["ساده"] = "simple", ["کامل"] = "complete", ["سال"] = "year", ["ماه"] = "month", ["روز"] = "day", ["امروز"] = "today",
        ["لوازم"] = "accessories", ["جانبی"] = "accessories", ["اکسسوری"] = "accessories", ["دیجیتال"] = "digital", ["الکترونیک"] = "electronics",
        ["خانگی"] = "home", ["آشپزخانه"] = "kitchen", ["شخصی"] = "personal", ["آرایشی"] = "cosmetic", ["بهداشتی"] = "hygiene",
        ["اداری"] = "office", ["تحریر"] = "stationery", ["صوتی"] = "audio", ["تصویری"] = "video", ["شبکه"] = "network",
        ["ذخیره سازی"] = "storage", ["جانبی موبایل"] = "mobile-accessories",
    };

    /// <summary>
    /// واژه‌نامهٔ نهایی: تک‌واژه‌ها + عبارت‌ها با کلیدهای یکسان‌شده (همان یکسان‌سازی متن ورودی: «آ» ← «ا»، بدون نیم‌فاصله،
    /// ي/ك عربی ← ی/ک). عبارت‌ها روی تک‌واژه‌های هم‌نام اولویت دارند.
    /// </summary>
    private static readonly Dictionary<string, string> Lexicon = BuildLexicon();

    private static Dictionary<string, string> BuildLexicon()
    {
        var lexicon = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in Words)
            lexicon[NormalizeKey(key)] = value;
        foreach (var (key, value) in Phrases)
            lexicon[NormalizeKey(key)] = value;
        return lexicon;
    }

    private static IEnumerable<string> NormalizeKeys(IEnumerable<string> keys) => keys.Select(NormalizeKey);

    /// <summary>یکسان‌سازی کلید واژه‌نامه مثل متن ورودی (FormD، حذف اعراب و نیم‌فاصله، فاصلهٔ تکی).</summary>
    public static string NormalizeKey(string key)
    {
        var builder = new StringBuilder(key.Length);
        var space = false;
        foreach (var raw in key.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark || raw is '\u200C' or '\u200D' or 'ـ')
                continue;
            var c = NormalizeChar(raw);
            if (char.IsWhiteSpace(c))
            {
                if (builder.Length > 0 && !space)
                    builder.Append(' ');
                space = true;
                continue;
            }
            space = false;
            builder.Append(c);
        }
        return builder.ToString().TrimEnd();
    }

    /// <summary>صفت‌های انگلیسی که پیش از اسم می‌آیند.</summary>
    private static readonly HashSet<string> Adjectives = new(StringComparer.Ordinal)
    {
        "smart", "fast", "wireless", "bluetooth", "magnetic", "original", "new", "portable", "gaming", "mini", "small", "large",
        "leather", "silicone", "metal", "plastic", "clear", "waterproof", "shockproof", "foldable", "rotating", "touch", "digital",
        "rechargeable", "electric", "sport", "classic", "fancy", "luxury", "wall", "car", "dual", "black", "white", "blue", "red",
        "green", "yellow", "orange", "pink", "purple", "gold", "silver", "gray", "cream", "rose-gold", "navy", "brown", "lilac",
        "maroon", "beige", "noise-cancelling", "fast-charging", "desktop", "cheap", "external", "internal",
    };

    /// <summary>اسم‌هایی که صفت پس از آن‌ها جابه‌جا می‌شود (برای جلوگیری از جابه‌جایی اشتباه در اسم خاص‌ها).</summary>
    private static readonly HashSet<string> Nouns = new(StringComparer.Ordinal)
    {
        "phone", "mobile", "tablet", "laptop", "notebook", "computer", "monitor", "keyboard", "mouse", "headphones", "earbuds", "headset",
        "earphones", "speaker", "microphone", "charger", "adapter", "cable", "converter", "hub", "power-bank", "battery", "watch",
        "band", "bracelet", "case", "cover", "bag", "backpack", "glass", "protector", "strap", "stand", "holder", "lens", "camera",
        "flash", "memory", "modem", "router", "console", "controller", "gamepad", "printer", "speaker", "tv", "lamp", "bulb", "fan",
        "iron", "pen", "card", "charging-cable", "screen-protector", "phone-case", "cooler", "scale", "toy", "robot", "drone",
        "hard-drive", "flash-drive", "memory-card", "smartwatch", "charging", "charge",
    };
}
