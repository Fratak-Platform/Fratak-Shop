using System.Text;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// دور ۲۲: «دروازهٔ کیفیت» نام دسته‌ها و ابزار خواندن مسیر دستهٔ سایت‌های دیگر.
/// <para>
/// خواستهٔ کاربر: «اسم دسته‌ها درست، کامل و اصولی باشد؛ نصفه و آشغال نه». پس هر نامی که دسته‌بندی هوشمند می‌خواهد بسازد
/// (از خوشه‌بندی نام کالاها یا از مسیر دستهٔ دیجی‌کالا/سایت مبدأ) اول از <see cref="Proper"/> می‌گذرد:
/// نیم‌فاصلهٔ درست می‌گیرد («بی سیم» ← «بی‌سیم»)، و اگر عدد، برند، واژهٔ تبلیغاتی، حرف ربط آویزان یا نشانهٔ «سایر/متفرقه» داشته باشد رد می‌شود.
/// </para>
/// <para>محل استفاده: <see cref="SmartCategoryService"/> (ساخت دسته)، <see cref="ProductNaming"/> (نیم‌فاصلهٔ نام کالا) و ProductIntakeService.</para>
/// </summary>
public static class CategoryNames
{
    /// <summary>بیشترین طول نام دسته‌ای که سیستم می‌سازد.</summary>
    public const int MaxLength = 40;

    private const char Zwnj = '\u200c';

    /// <summary>
    /// ترکیب‌های رایجی که در نام کالا/دسته با فاصلهٔ معمولی نوشته می‌شوند ولی شکل درستشان نیم‌فاصله یا سرهم است.
    /// فقط ترکیب‌های قطعی (هیچ معنای دیگری با فاصله ندارند).
    /// </summary>
    private static readonly (string From, string To)[] Joins =
    [
        ("بی سیم", "بی\u200cسیم"), ("لپ تاپ", "لپ\u200cتاپ"), ("سرخ کن", "سرخ\u200cکن"), ("چای ساز", "چای\u200cساز"),
        ("قهوه ساز", "قهوه\u200cساز"), ("اسپرسو ساز", "اسپرسوساز"), ("مخلوط کن", "مخلوط\u200cکن"), ("آبمیوه گیری", "آبمیوه\u200cگیری"),
        ("اسباب بازی", "اسباب\u200cبازی"), ("کوله پشتی", "کوله\u200cپشتی"), ("مچ بند", "مچ\u200cبند"), ("صفحه نمایش", "صفحه\u200cنمایش"),
        ("سه راهی", "سه\u200cراهی"), ("چند راهی", "چندراهی"), ("ریش تراش", "ریش\u200cتراش"), ("جارو برقی", "جاروبرقی"),
        ("پاور بانک", "پاوربانک"), ("هند زفری", "هندزفری"), ("گیم پد", "گیم\u200cپد"), ("نوت بوک", "نوت\u200cبوک"),
        ("مک بوک", "مک\u200cبوک"), ("ماساژ دهنده", "ماساژدهنده"), ("حالت دهنده", "حالت\u200cدهنده"), ("فر کننده", "فرکننده"),
        ("خوش بو کننده", "خوشبوکننده"), ("ضد آب", "ضدآب"), ("ضد ضربه", "ضدضربه"), ("پلی استیشن", "پلی\u200cاستیشن"),
        ("دست ساز", "دست\u200cساز"), ("یخچال فریزر", "یخچال\u200cفریزر"), ("ظرف شویی", "ظرفشویی"), ("لباس شویی", "لباسشویی"),
    ];

    private static readonly Regex[] JoinPatterns = Joins
        .Select(j => new Regex("(?<![\\p{L}\\u200c])" + Regex.Escape(j.From).Replace("\\ ", "[ \\u200c]+") + "(?![\\p{L}])",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        .ToArray();

    // «کتاب ها» / «کتابها» ← «کتاب‌ها»؛ «کتاب های» ← «کتاب‌های». فقط بعد از واژهٔ فارسی ≥ ۲ حرف.
    private static readonly Regex Plural = new("(?<=[\\u0600-\\u06FF]{2}) (ها|های|هایی)(?![\\p{L}])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>واژه‌های تبلیغاتی که هرگز در نام دسته نمی‌آیند (نرمال‌شده، بدون نیم‌فاصله).</summary>
    private static readonly HashSet<string> Marketing = new(StringComparer.Ordinal)
    {
        "ویژه", "حراج", "تخفیف", "تخفیفدار", "پرفروش", "پرفروشترین", "شگفت", "شگفتانگیز", "پیشنهاد", "پیشنهادی", "جشنواره", "برترین", "بهترین",
        "ارزان", "ارزانترین", "اورجینال", "جدید", "جدیدترین", "رایگان", "فروش", "خرید", "قیمت", "new", "sale", "best", "hot", "top", "offer",
    };

    /// <summary>واژه‌هایی که نام دسته با آن‌ها شروع یا تمام نمی‌شود (نام نصفه).</summary>
    private static readonly HashSet<string> Dangling = new(StringComparer.Ordinal)
    {
        "و", "با", "برای", "از", "در", "به", "یا", "بدون", "مخصوص", "همراه", "مدل", "سری", "طرح", "نوع", "کد",
    };

    /// <summary>واژه‌های بی‌معنی سایت‌ها در مسیر دسته («خانه»، «فروشگاه» …) که بخشی از دسته نیستند.</summary>
    private static readonly HashSet<string> PathNoise = new(StringComparer.Ordinal)
    {
        "خانه", "صفحه اصلی", "فروشگاه", "محصولات", "همه محصولات", "دیجی کالا", "دیجیکالا", "home", "shop", "products", "all", "store",
    };

    /// <summary>
    /// نیم‌فاصله و املای درست. مثال: «هندزفری بی سیم» ← «هندزفری بی‌سیم»؛ «لپ تاپ ها» ← «لپ‌تاپ‌ها»؛ «ي/ك» عربی ← «ی/ک».
    /// متن انگلیسی دست نمی‌خورد.
    /// </summary>
    public static string FixSpacing(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var value = text.Replace('ي', 'ی').Replace('ك', 'ک').Replace('\u00A0', ' ');
        value = Regex.Replace(value, "[ \\t\\r\\n]+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Trim();
        try
        {
            for (var i = 0; i < JoinPatterns.Length; i++)
                value = JoinPatterns[i].Replace(value, Joins[i].To);
            value = Plural.Replace(value, m => Zwnj + m.Groups[1].Value);
        }
        catch (RegexMatchTimeoutException)
        {
            // متن غیرعادی: همان متن تمیزشده (بدون اصلاح نیم‌فاصله) برمی‌گردد.
        }
        return value;
    }

    /// <summary>
    /// نام دستهٔ درست و کامل یا null (رد). قاعده‌ها:
    /// <list type="bullet">
    /// <item>فقط حروف فارسی (بدون عدد، بدون حرف لاتین؛ نام دسته‌های فروشگاه فارسی است).</item>
    /// <item>۲ تا ۴۰ حرف و حداکثر ۵ واژه؛ هر واژه دست‌کم ۲ حرف (جز «و»).</item>
    /// <item>بدون «سایر/متفرقه/دسته‌بندی نشده»، بدون واژهٔ تبلیغاتی («ویژه»، «حراج»، «اورجینال»)، بدون نام برند.</item>
    /// <item>شروع/پایان با حرف ربط یا «مدل/طرح» = نام نصفه ← رد.</item>
    /// <item>همهٔ واژه‌ها رنگ/صفت ← رد (از فهرست دسته‌بند).</item>
    /// </list>
    /// مثال‌ها: «ماساژور» ✓؛ «لوازم شخصی برقی» ✓؛ «پاور بانک» ← «پاوربانک»؛ «سایر» ✗؛ «حراج ویژه» ✗؛ «هدفون و» ✗؛ «سامسونگ» (برند) ✗؛ «A55» ✗.
    /// </summary>
    /// <param name="raw">نام پیشنهادی.</param>
    /// <param name="brands">نام برندهای فروشگاه (فارسی و انگلیسی) برای رد نام برند.</param>
    public static string? Proper(string? raw, IEnumerable<string>? brands = null)
    {
        var name = FixSpacing(raw).Trim(' ', '-', '_', '|', '،', ',', '.', ':', ';', '/', '\\', '(', ')', '[', ']', '"', '\'', '«', '»', '>', '<', '*');
        if (name.Length is < 2 or > MaxLength)
            return null;
        if (name.Any(char.IsDigit) || name.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
            return null;
        if (name.Any(c => !(PersianText.IsPersianLetter(c) || c == ' ' || c == Zwnj) || c is '،' or '؛' or '؟' or 'ـ'))
            return null;
        if (CategoryClassifier.IsCatchAll(name))
            return null;

        var words = PersianText.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 5)
            return null;
        if (words.Any(w => w.Length < 2 && w != "و"))
            return null;
        if (Dangling.Contains(words[0]) || Dangling.Contains(words[^1]))
            return null;

        // واژه‌های تبلیغاتی با و بدون نیم‌فاصله («شگفت‌انگیز» ← «شگفت انگیز» و «شگفتانگیز»).
        var compact = PersianText.Normalize(name).Replace(" ", "", StringComparison.Ordinal);
        if (words.Any(Marketing.Contains) || Marketing.Any(m => m.Length >= 5 && compact.Contains(m, StringComparison.Ordinal)))
            return null;

        if (brands is not null)
        {
            var brandWords = brands.Select(PersianText.Normalize).Where(b => b.Length >= 2).ToHashSet(StringComparer.Ordinal);
            var normalized = PersianText.Normalize(name);
            if (brandWords.Contains(normalized) || words.Any(w => w.Length >= 3 && brandWords.Contains(w)))
                return null;
        }

        if (words.All(w => w == "و" || CategoryClassifier.IsAdjective(w)))
            return null;
        return name;
    }

    /// <summary>
    /// خواندن مسیر دستهٔ سایت دیگر. جداکننده‌ها: «>»، «»»، «/»، «|». بخش‌های بی‌معنی («خانه»، «فروشگاه») و تکراری حذف و هر بخش تمیز می‌شود.
    /// مثال: «خانه > زیبایی و سلامت > لوازم شخصی برقی > ماساژور» ← [زیبایی و سلامت، لوازم شخصی برقی، ماساژور].
    /// حداکثر ۶ بخش، هر کدام ≤ ۶۰ حرف.
    /// </summary>
    public static IReadOnlyList<string> Path(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return [];
        var result = new List<string>();
        foreach (var part in source.Split(['>', '»', '/', '|', '\\'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = FixSpacing(part).Trim(' ', '-', '،', ',', '.', ':');
            if (clean.Length is < 2 or > 60 || PathNoise.Contains(PersianText.Normalize(clean)))
                continue;
            if (result.Count > 0 && PersianText.Normalize(result[^1]) == PersianText.Normalize(clean))
                continue;
            result.Add(clean);
            if (result.Count == 6)
                break;
        }
        return result;
    }

    /// <summary>ساخت متن مسیر برای ذخیره. مثال: [الف، ب] ← «الف > ب».</summary>
    public static string Join(IEnumerable<string> segments) => string.Join(" > ", segments);

    /// <summary>
    /// آیا یک بخش مسیر (یا نام پیشنهادی) همان دستهٔ موجود است؟ نام یکسان (بدون توجه به نیم‌فاصله و جمع/مفرد)،
    /// یا هر دو یک «نوع کالا» هستند. مثال: («ماساژورها»، «ماساژور») ← بله؛ («هندزفری بی سیم»، «هندزفری بی‌سیم») ← بله؛
    /// («لوازم جانبی»، «لوازم جانبی موبایل») ← خیر (یکی کلی‌تر است).
    /// </summary>
    public static bool Same(string? segment, string? categoryName, string? categorySlug = null)
    {
        if (string.IsNullOrWhiteSpace(segment) || string.IsNullOrWhiteSpace(categoryName))
            return false;
        if (PersianText.Normalize(segment) == PersianText.Normalize(categoryName))
            return true;
        var a = CategoryClassifier.Terms(segment);
        var b = CategoryClassifier.Terms(categoryName);
        if (a.Count > 0 && a.Count == b.Count && a.ToHashSet(StringComparer.Ordinal).SetEquals(b))
            return true;
        return ProductTypes.DetectHead(segment) is { } type && ProductTypes.NameMatches(type, categoryName, categorySlug);
    }

    /// <summary>
    /// متن کوتاه‌شده تا سقف طول (برای ستون‌های پایگاه داده). مثال: Truncate("abc", 2) ← "ab"؛ null یا فاصلهٔ خالی ← null.
    /// </summary>
    public static string? Truncate(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var value = text.Trim();
        return value.Length <= max ? value : value[..max].TrimEnd();
    }

    /// <summary>متن خلاصهٔ مسیر برای نمایش در پنل. مثال: [الف، ب، ج] ← «الف › ب › ج».</summary>
    public static string Pretty(IReadOnlyList<string> path)
    {
        var builder = new StringBuilder();
        foreach (var part in path)
        {
            if (builder.Length > 0)
                builder.Append(" › ");
            builder.Append(part);
        }
        return builder.ToString();
    }
}
