using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// نتیجهٔ بهینه‌سازی نام کالا. <see cref="Changes"/> فهرست کوتاه کارهای انجام‌شده (برای نمایش به ادمین)؛ خالی یعنی نام خوب بود.
/// مثال: Name=«هدفون بی‌سیم شیائومی مدل Redmi Buds 4»، Changes=[«حذف عبارت تبلیغاتی»، «افزودن «مدل» پیش از نام مدل»].
/// </summary>
public sealed record NameSuggestion(string Name, IReadOnlyList<string> Changes)
{
    /// <summary>آیا نام تغییری کرد؟</summary>
    public bool Changed => Changes.Count > 0;
}

/// <summary>
/// دور ۲۲: بهینه‌سازی نام کالا، ساخت «نام انگلیسی» و نامک — کاملاً قاعده‌محور و داخلی (بدون هوش مصنوعی بیرونی).
/// <para>
/// اصل مهم: هیچ اطلاعاتی ساخته نمی‌شود. نام فقط تمیز می‌شود (حذف شکلک، تبلیغ، «خرید» اول نام، تکرار، نیم‌فاصلهٔ غلط) و
/// نام انگلیسی فقط از عنوان انگلیسی واقعی (دیجی‌کالا) یا واژه‌های لاتین خود نام + برند + نوع کالا ساخته می‌شود؛
/// اگر نام مدل لاتینی در کار نباشد نام انگلیسی ساخته نمی‌شود (حدس نمی‌زنیم).
/// </para>
/// <para>محل استفاده: کار «تکمیل هوشمند کالا» (SmartOrchestrator) و آزمون‌های واحد.</para>
/// </summary>
public static class ProductNaming
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    private const string Edge = "(?<![\\p{L}\\p{N}\\u200c])";
    private const string EdgeEnd = "(?![\\p{L}\\p{N}\\u200c])";

    /// <summary>عبارت‌های تبلیغاتی که جای نام کالا نیستند (گوگل هم عنوان پر از تبلیغ را دوست ندارد).</summary>
    private static readonly string[] MarketingPhrases =
    [
        "ارسال رایگان", "فروش ویژه", "تخفیف ویژه", "پیشنهاد ویژه", "شگفت انگیز", "حراج", "پرفروش", "اورجینال", "اورجینا", "original", "orginal",
        "100% اصل", "۱۰۰٪ اصل", "اصل", "با کیفیت", "باکیفیت", "کیفیت عالی", "درجه یک", "ارزان", "قیمت عالی", "موجود شد",
    ];

    private static readonly Regex Marketing = new(
        Edge + "(" + string.Join("|", MarketingPhrases.OrderByDescending(p => p.Length).Select(p => Regex.Escape(p).Replace("\\ ", "[\\s\\u200c]*"))) + ")" + EdgeEnd,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);

    private static readonly Regex BuyPrefix = new("^(قیمت\\s*و\\s*خرید|خرید\\s*و\\s*قیمت|خرید\\s*آنلاین|خرید|قیمت)\\s+", RegexOptions.CultureInvariant, Timeout);

    private static readonly Regex LatinRun = new("[A-Za-z][A-Za-z0-9\\-\\+\\./]*(?:\\s+[A-Za-z0-9][A-Za-z0-9\\-\\+\\./]*)*", RegexOptions.CultureInvariant, Timeout);

    /// <summary>واژه‌های فنی لاتین که نام مدل نیستند («کابل USB-C» نباید «کابل مدل USB-C» شود).</summary>
    private static readonly HashSet<string> TechWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "usb", "usb-c", "type-c", "typec", "hdmi", "vga", "led", "lcd", "oled", "amoled", "wifi", "wi-fi", "bluetooth", "ssd", "hdd", "rgb", "ram",
        "gb", "tb", "mah", "w", "v", "hz", "4k", "8k", "5g", "4g", "nfc", "gps", "anc", "enc", "aux", "otg", "pd", "qc", "ip67", "ip68", "ipx4", "ipx5",
        "ipx7", "magsafe", "lightning", "micro", "mini", "pro", "max", "plus", "uv", "dc", "ac",
    };

    /// <summary>
    /// بهینه‌سازی نام کالا. هر قاعده فقط وقتی اجرا می‌شود که لازم باشد؛ نام خوب دست نمی‌خورد.
    /// <list type="number">
    /// <item>حذف شکلک و نشانه‌ها (★ 🔥 ™ ® !!! ؟).</item>
    /// <item>حذف «خرید/قیمت» از ابتدای نام («خرید هدفون …» ← «هدفون …»).</item>
    /// <item>حذف عبارت‌های تبلیغاتی («اورجینال»، «ارسال رایگان»، «حراج»).</item>
    /// <item>حذف پرانتز خالی، جداکنندهٔ آویزان و واژهٔ تکراری پشت‌سرهم.</item>
    /// <item>نام فقط لاتین + نوع کالا معلوم ← نوع فارسی اول نام («Redmi Buds 4» ← «هدفون Redmi Buds 4»).</item>
    /// <item>«مدل» پیش از نام مدل لاتین («گوشی سامسونگ Galaxy A55» ← «گوشی سامسونگ مدل Galaxy A55»)؛ نه پیش از برند یا واژهٔ فنی.</item>
    /// <item>نیم‌فاصلهٔ درست («بی سیم» ← «بی‌سیم»).</item>
    /// </list>
    /// نتیجهٔ کوتاه‌تر از ۳ حرف ← نام اصلی بدون تغییر (هرگز نام را خراب نمی‌کند).
    /// </summary>
    /// <param name="name">نام فعلی.</param>
    /// <param name="type">نوع کالا (از نام یا دسته)؛ برای نام‌های فقط لاتین.</param>
    /// <param name="brandEnglish">نام انگلیسی برند (اگر معلوم است)؛ «مدل» پیش از برند نمی‌آید.</param>
    public static NameSuggestion Refine(string? name, ProductType? type = null, string? brandEnglish = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new NameSuggestion(name?.Trim() ?? "", []);
        var original = Collapse(name);
        var changes = new List<string>();
        var value = original;
        try
        {
            var cleaned = StripSymbols(value);
            if (cleaned != value)
            {
                changes.Add("حذف شکلک و نشانه‌های اضافه");
                value = cleaned;
            }

            var noPrefix = BuyPrefix.Replace(value, "");
            if (noPrefix != value)
            {
                changes.Add("حذف «خرید/قیمت» از ابتدای نام");
                value = noPrefix;
            }

            var noAds = Collapse(Marketing.Replace(value, " "));
            if (noAds != Collapse(value))
            {
                changes.Add("حذف عبارت تبلیغاتی");
                value = noAds;
            }

            var tidy = Tidy(value);
            if (tidy != value)
            {
                if (!changes.Contains("حذف عبارت تبلیغاتی"))
                    changes.Add("مرتب کردن جداکننده‌ها و حذف تکرار");
                value = tidy;
            }

            if (type is not null && !value.Any(IsPersian) && value.Any(char.IsLetter))
            {
                value = type.Head + " " + value;
                changes.Add($"افزودن نوع کالا («{type.Head}») به ابتدای نام");
            }

            var withModel = AddModelWord(value, brandEnglish);
            if (withModel != value)
            {
                changes.Add("افزودن «مدل» پیش از نام مدل");
                value = withModel;
            }

            var spaced = CategoryNames.FixSpacing(value);
            if (spaced != value)
            {
                changes.Add("اصلاح نیم‌فاصله");
                value = spaced;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return new NameSuggestion(original, []);
        }

        value = Cut(Collapse(value), 150);
        if (value.Length < 3 || value == original)
            return new NameSuggestion(original, []);
        return new NameSuggestion(value, changes);
    }

    /// <summary>
    /// نام انگلیسی کالا — هرگز حدسی نیست:
    /// <list type="number">
    /// <item>عنوان انگلیسی واقعی همین کالا (مثلاً از دیجی‌کالا) اگر حرف لاتین دارد ← همان (تمیزشده).</item>
    /// <item>وگرنه واژه‌های لاتین خود نام («Galaxy A55») + برند انگلیسی (اگر در نام نیست) + نوع کالا به انگلیسی.</item>
    /// <item>نام بدون هیچ واژهٔ لاتین ← null (نام انگلیسی ساخته نمی‌شود؛ ترجمهٔ ماشینی نام فارسی جعلی است).</item>
    /// </list>
    /// مثال: («گوشی موبایل سامسونگ مدل Galaxy A55»، برند Samsung، نوع گوشی) ← «Samsung Galaxy A55 Mobile Phone».
    /// </summary>
    public static string? EnglishName(string? persianName, string? brandEnglish, ProductType? type, string? knownEnglishTitle = null)
    {
        if (!string.IsNullOrWhiteSpace(knownEnglishTitle) && knownEnglishTitle.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
        {
            var clean = Collapse(new string(knownEnglishTitle.Where(c => c < 0x2000 && !char.IsControl(c)).ToArray()));
            if (clean.Length >= 3)
                return Cut(clean, 200);
        }
        if (string.IsNullOrWhiteSpace(persianName))
            return null;

        string[] runs;
        try
        {
            runs = LatinRun.Matches(persianName).Select(m => m.Value.Trim()).Where(r => r.Length > 0).ToArray();
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
        var latin = string.Join(' ', runs);
        // فقط واژهٔ فنی («USB-C»، «LED») نام مدل نیست.
        if (latin.Length < 2 || latin.Split(' ').All(w => TechWords.Contains(w)))
            return null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(brandEnglish) && brandEnglish.Any(char.IsAsciiLetter)
                                                     && !latin.Contains(brandEnglish.Trim(), StringComparison.OrdinalIgnoreCase))
            parts.Add(brandEnglish.Trim());
        parts.Add(latin);
        if (type is not null && !latin.Contains(type.English, StringComparison.OrdinalIgnoreCase))
            parts.Add(type.English);
        return Cut(Collapse(string.Join(' ', parts)), 200);
    }

    /// <summary>
    /// دور ۲۸: نام انگلیسی فعلی قابل استفاده است؟ (دست‌کم ۳ نویسه و حرف لاتین، بدون حرف فارسی/عربی.)
    /// مثال: «Samsung Galaxy A55» ← true؛ «گوشی سامسونگ» ← false؛ null ← false.
    /// </summary>
    public static bool IsUsableEnglish(string? englishName) =>
        !string.IsNullOrWhiteSpace(englishName) && englishName.Trim().Length >= 3
        && englishName.Any(char.IsAsciiLetter) && !englishName.Any(c => c is >= '\u0600' and <= '\u06FF');

    /// <summary>
    /// دور ۲۸ – آخرین راه نام انگلیسی (وقتی دیجی‌کالا، ترب و واژه‌های لاتین خود نام چیزی ندادند): واژه‌نامهٔ داخلی نامک
    /// (<see cref="EnglishSlug"/>؛ همان که نامک انگلیسی صفحه‌ها را می‌سازد) + برند انگلیسی + نام انگلیسی نوع کالا، با حروف بزرگ اول هر واژه.
    /// واژه‌ای که در واژه‌نامه نیست با آوانویسی می‌آید (مثلاً نام خاص). کمتر از ۳ حرف ← null.
    /// مثال: («کتری برقی پارس خزر»، «Pars Khazar»، نوع kettle) ← «Pars Khazar Electric Kettle».
    /// </summary>
    public static string? EnglishFromDictionary(string? persianName, string? brandEnglish, ProductType? type)
    {
        if (string.IsNullOrWhiteSpace(persianName))
            return null;
        var words = EnglishSlug.From(persianName, 120).Split('-', StringSplitOptions.RemoveEmptyEntries).ToList();
        var parts = new List<string>();
        var brand = !string.IsNullOrWhiteSpace(brandEnglish) && brandEnglish.Any(char.IsAsciiLetter) ? brandEnglish.Trim() : null;
        if (brand is not null)
        {
            parts.Add(brand);
            // آوانویسی برند فارسی («pars-khazar») تکرار نشود.
            var brandWords = EnglishSlug.From(brand).Split('-', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            words.RemoveAll(brandWords.Contains);
        }
        var typeWords = type is null ? new HashSet<string>(StringComparer.Ordinal) : type.Key.Split('-').ToHashSet(StringComparer.Ordinal);
        var rest = words.Where(w => !typeWords.Contains(w) && w.Length > 1 && !NoiseWords.Contains(w)).Take(6)
            .Select(w => char.IsAsciiDigit(w[0]) ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..]).ToList();
        parts.AddRange(rest);
        if (type is not null)
            parts.Add(type.English);
        var value = Collapse(string.Join(' ', parts));
        return value.Length >= 3 && value.Any(char.IsAsciiLetter) ? Cut(value, 200) : null;
    }

    // واژه‌های بی‌معنا در نام انگلیسی («model»، «with» …) که از واژه‌نامهٔ نامک می‌آیند.
    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal) { "model", "with", "and", "for", "the", "of", "code" };

    /// <summary>
    /// نامک انگلیسی پیشنهادی: اول از نام انگلیسی، وگرنه برگردان نام فارسی (واژه‌نامهٔ داخلی <see cref="EnglishSlug"/>).
    /// کمتر از ۳ حرف یا نامعتبر ← null. مثال: («…»، «Samsung Galaxy A55 Mobile Phone») ← «samsung-galaxy-a55-mobile-phone».
    /// </summary>
    public static string? Slug(string? name, string? englishName)
    {
        var slug = EnglishSlug.Prefer(name, englishName);
        return slug.Length >= 3 && EnglishSlug.IsEnglish(slug) ? slug : null;
    }

    // ── ابزارها ──

    private static bool IsPersian(char c) => c is >= '\u0600' and <= '\u06FF';

    private static string Collapse(string text) => string.Join(' ', text.Split([' ', '\t', '\r', '\n', '\u00A0'], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>حذف شکلک‌ها، نشانه‌های تزئینی و علامت‌های تکراری. «+»، «%»، «/»، «-»، «()» و «°» می‌مانند.</summary>
    private static string StripSymbols(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsSurrogate(c) || c is '!' or '؟' or '?' or '*' or '~' or '#' or '^' or '=' or '…' or '|' or '\u200d' or '\uFE0F')
            {
                builder.Append(' ');
                continue;
            }
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if ((category is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol && c != '°') || char.IsControl(c))
            {
                builder.Append(' ');
                continue;
            }
            builder.Append(c);
        }
        return Collapse(builder.ToString());
    }

    /// <summary>پرانتز خالی، جداکنندهٔ ابتدا/انتها، جداکنندهٔ تکراری و واژهٔ تکراری پشت‌سرهم.</summary>
    private static string Tidy(string text)
    {
        var value = Regex.Replace(text, "\\(\\s*\\)|\\[\\s*\\]|\\{\\s*\\}|«\\s*»", " ", RegexOptions.None, Timeout);
        value = Regex.Replace(value, "\\s*([-–—،,:/])\\s*(?:[-–—،,:/]\\s*)+", " $1 ", RegexOptions.None, Timeout);
        value = Collapse(value).Trim(' ', '-', '–', '—', '،', ',', ':', '/', '.');
        var words = value.Split(' ');
        var kept = new List<string>(words.Length);
        foreach (var word in words)
        {
            if (kept.Count > 0 && string.Equals(Seo.Engine.PersianText.Normalize(kept[^1]), Seo.Engine.PersianText.Normalize(word), StringComparison.Ordinal)
                               && word.Any(char.IsLetter))
                continue;
            kept.Add(word);
        }
        return string.Join(' ', kept);
    }

    /// <summary>
    /// «مدل» پیش از اولین بخش لاتین که بعد از واژهٔ فارسی آمده و رقم دارد، اگر «مدل» نیست و بخش لاتین با برند یا واژهٔ فنی شروع نمی‌شود.
    /// مثال: «هدفون شیائومی Redmi Buds 4» ← «هدفون شیائومی مدل Redmi Buds 4»؛ «پاوربانک Anker 10000» (برند) ← بدون تغییر.
    /// </summary>
    private static string AddModelWord(string text, string? brandEnglish)
    {
        if (!text.Any(IsPersian) || Regex.IsMatch(text, Edge + "مدل" + EdgeEnd, RegexOptions.None, Timeout))
            return text;
        var match = LatinRun.Match(text);
        if (!match.Success || match.Index == 0 || !text[..match.Index].Any(IsPersian) || !match.Value.Any(char.IsDigit))
            return text;
        var first = match.Value.Split(' ')[0];
        if (TechWords.Contains(first) || first.Any(char.IsDigit) && first.All(c => char.IsDigit(c) || c is 'k' or 'K' or 'g' or 'G' or 'w' or 'W'))
            return text;
        if (!string.IsNullOrWhiteSpace(brandEnglish) && first.Equals(brandEnglish.Trim().Split(' ')[0], StringComparison.OrdinalIgnoreCase))
            return text;
        return text[..match.Index] + "مدل " + text[match.Index..];
    }

    private static string Cut(string text, int max)
    {
        if (text.Length <= max)
            return text;
        var cut = text[..max];
        var space = cut.LastIndexOf(' ');
        return (space > max / 2 ? cut[..space] : cut).Trim();
    }
}
