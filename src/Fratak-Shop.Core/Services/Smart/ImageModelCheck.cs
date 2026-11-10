using System.Text;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>نتیجهٔ بررسی مدل منبع عکس. عددها در نتیجهٔ ذخیره‌شدهٔ صف هوشمند می‌مانند؛ تغییر ندهید.</summary>
public enum ModelVerdict
{
    /// <summary>منبع کد مدلی ندارد یا کالای ما کد مدل ندارد؛ تصمیم با کارشناس.</summary>
    Unknown = 0,

    /// <summary>کد مدل منبع (عنوان یا ردیف «مدل» مشخصات همان صفحه) دقیقاً همان مدل کالای ماست.</summary>
    Same = 1,

    /// <summary>منبع مدل دیگری از همان خانواده است (مثلاً A35 به‌جای A55، یا «Pro» وقتی کالای ما Pro نیست) ← عکس کنار می‌رود.</summary>
    Different = 2,
}

/// <summary>
/// تأیید خودکار «عکس و مشخصات دقیقاً مال همین مدل است» (درخواست دور ۲۳) – بدون هوش مصنوعی، فقط با کد مدل.
/// <para>منبع هر عکس پیشنهادی (عنوان صفحه/کارت + مقدار ردیف «مدل» در جدول مشخصات همان صفحه) با نام کالای ما مقایسه می‌شود:</para>
/// <list type="bullet">
/// <item>کد مدل = واژهٔ لاتینِ دارای حرف و رقم (A55، WH-1000XM5 ← wh1000xm5) یا «واژهٔ لاتین + عدد» پشت سر هم (iPhone 15 ← iphone15).
/// واحدها (128GB، 5000mAh، 65W، 5G، 4K) کد مدل نیستند.</item>
/// <item>«همان»: یکی از کدهای کالای ما در منبع هست و منبع نسخهٔ دیگری (Pro/Plus/Max/Ultra/Mini/Lite/FE) ندارد که کالای ما ندارد.</item>
/// <item>«متفاوت»: منبع کد مدلی از <b>همان خانواده</b> دارد (همان الگوی حرف‌ها، مثل a# و a#) ولی عدد فرق دارد، یا نسخهٔ اضافه دارد.
/// کدی از خانوادهٔ دیگر (مثل USB3 در کنار A1263) «متفاوت» حساب نمی‌شود تا عکس درست بی‌دلیل حذف نشود.</item>
/// </list>
/// <para>مثال: کالا «گوشی سامسونگ Galaxy A55» ← «Samsung Galaxy A55 5G» = همان؛ «Galaxy A35» = متفاوت؛ «گوشی سامسونگ» = نامعلوم.</para>
/// <para>محل استفاده: SmartImageFinder (عکس «متفاوت» اصلاً دانلود نمی‌شود؛ «همان» نشان سبز «مدل تأیید شد» در صفحهٔ تکمیل هوشمند کالا می‌گیرد).
/// تأیید نهایی همیشه با کارشناس است.</para>
/// </summary>
public static partial class ImageModelCheck
{
    /// <summary>واژه‌های نسخه (فارسی به معادل لاتین نگاشت می‌شود).</summary>
    private static readonly Dictionary<string, string> Variants = new(StringComparer.Ordinal)
    {
        ["pro"] = "pro", ["plus"] = "plus", ["max"] = "max", ["ultra"] = "ultra", ["mini"] = "mini", ["lite"] = "lite", ["fe"] = "fe",
        ["پرو"] = "pro", ["پلاس"] = "plus", ["مکس"] = "max", ["الترا"] = "ultra", ["اولترا"] = "ultra", ["مینی"] = "mini", ["لایت"] = "lite",
    };

    /// <summary>
    /// بررسی یک منبع. <paramref name="sourceText"/> = عنوان صفحه/کارت (+ مقدار ردیف «مدل» مشخصات همان صفحه، اگر هست).
    /// مثال: Check("هدفون سونی WH-1000XM5", null, "Sony WH1000XM4 Black") ← Different.
    /// </summary>
    public static ModelVerdict Check(string productName, string? englishName, string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return ModelVerdict.Unknown;
        var product = Codes(productName + " " + englishName);
        if (product.Count == 0)
            return ModelVerdict.Unknown;
        var source = Codes(sourceText);
        var productVariants = VariantsOf(productName + " " + englishName);
        var sourceVariants = VariantsOf(sourceText);

        if (product.Overlaps(source))
        {
            if (!sourceVariants.IsSubsetOf(productVariants))
                return ModelVerdict.Different;   // منبع «Pro» است ولی کالای ما نه
            return productVariants.IsSubsetOf(sourceVariants) ? ModelVerdict.Same : ModelVerdict.Unknown;
        }
        var families = product.Select(Skeleton).ToHashSet(StringComparer.Ordinal);
        return source.Any(code => families.Contains(Skeleton(code))) ? ModelVerdict.Different : ModelVerdict.Unknown;
    }

    /// <summary>
    /// کدهای مدل یک متن (حروف کوچک، رقم لاتین، بدون خط تیره). مثال: «Sony WH-1000XM5 (2024)» ← {wh1000xm5}؛ «Apple iPhone 15 128GB» ← {iphone15}.
    /// </summary>
    public static HashSet<string> Codes(string? text)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
            return codes;
        var normalized = SearchText.NormalizeDigits(text).ToLowerInvariant().Replace("-", "").Replace("_", "");
        if (normalized.Length > 400)
            normalized = normalized[..400];
        var tokens = Splitter().Split(normalized).Where(t => t.Length > 0).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (IsCode(token))
                codes.Add(token);
            // «iphone 15» ← iphone15؛ «wh 1000xm5» ← wh1000xm5 (واژهٔ تمام‌حرف + واژه‌ای که با رقم شروع می‌شود).
            if (i + 1 < tokens.Count && token.All(char.IsAsciiLetter) && token.Length is >= 2 and <= 12 && char.IsAsciiDigit(tokens[i + 1][0])
                && !Unit().IsMatch(tokens[i + 1]))
            {
                var joined = token + tokens[i + 1];
                if (joined.Length <= 24)
                    codes.Add(joined);
            }
        }
        return codes;
    }

    /// <summary>برچسب فارسی برای صفحهٔ مدیر. مثال: Same ← «مدل تأیید شد».</summary>
    public static string Label(ModelVerdict verdict) => verdict switch
    {
        ModelVerdict.Same => "مدل تأیید شد",
        ModelVerdict.Different => "مدل دیگر",
        _ => "مدل نامشخص",
    };

    /// <summary>آیا کلید ردیف مشخصات «مدل» است؟ مثال: «مدل»، «Model»، «شماره مدل» ← بله؛ «رنگ» و «مدل‌های سازگار» ← خیر.</summary>
    public static bool IsModelKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && key.Length <= 30
        && !key.Contains("سازگار", StringComparison.Ordinal) && !key.Contains("compatib", StringComparison.OrdinalIgnoreCase)
        && (key.Contains("مدل", StringComparison.Ordinal) || key.Contains("model", StringComparison.OrdinalIgnoreCase) || key.Contains("part number", StringComparison.OrdinalIgnoreCase));

    private static bool IsCode(string token) =>
        token.Length is >= 2 and <= 24 && token.Any(char.IsAsciiDigit) && token.Any(char.IsAsciiLetter) && !Unit().IsMatch(token);

    /// <summary>الگوی خانواده: رقم‌ها ← «#». مثال: a55 و a35 ← a#؛ wh1000xm5 ← wh#xm#؛ usb3 ← usb#.</summary>
    private static string Skeleton(string code)
    {
        var builder = new StringBuilder(code.Length);
        foreach (var c in code)
        {
            if (char.IsAsciiDigit(c))
            {
                if (builder.Length == 0 || builder[^1] != '#')
                    builder.Append('#');
            }
            else
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    private static HashSet<string> VariantsOf(string text)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (text.Length > 400)
            text = text[..400];
        foreach (var token in Splitter().Split(SearchText.NormalizeDigits(text).ToLowerInvariant().Replace('ي', 'ی')))
            if (Variants.TryGetValue(token, out var variant))
                result.Add(variant);
        return result;
    }

    /// <summary>جداکنندهٔ واژه‌ها: هر چیزی جز حرف (لاتین/فارسی) و رقم.</summary>
    [GeneratedRegex(@"[^a-z0-9\u0600-\u06FF]+", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Splitter();

    /// <summary>واحدها و پسوندهای ترتیبی که کد مدل نیستند. مثال: 128gb، 5000mah، 65w، 5g، 4k، 1080p، 2nd.</summary>
    [GeneratedRegex(@"^\d+(\.\d+)?(gb|tb|mb|kb|mah|ah|wh|w|kw|v|a|hz|khz|mhz|ghz|mm|cm|m|km|g|kg|mg|ml|l|in|inch|k|p|fps|mp|db|rpm|x|th|nd|rd|st|pcs|gen)$", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Unit();
}

/// <summary>
/// نام فایل انگلیسی و خوانا برای عکس کالا (پیشنهاد ۲۷، دور ۲۳) – خالص و آزمون‌پذیر.
/// <para>قاعده: نامک لاتین کالا (فقط a-z، 0-9 و خط تیره؛ حداکثر ۶۰ نویسه و بریده‌شده سر خط تیره) + «-» + ۸ نویسهٔ تصادفی هگز + پسوند.
/// بخش تصادفی جلوی هم‌نامی و حدس زدن را می‌گیرد. نامک غیرلاتین/خالی یا پسوند غیرمجاز ← null (نام قبلی می‌ماند).</para>
/// <para>مثال: Build("samsung-galaxy-a55", ".webp") ← «samsung-galaxy-a55-1a2b3c4d.webp». محل استفاده: SmartImageFinder.SeoRename.</para>
/// </summary>
public static class SeoImageName
{
    /// <summary>بیشترین طول بخش نامک.</summary>
    public const int MaxSlugLength = 60;

    private static readonly HashSet<string> Extensions = new(StringComparer.Ordinal) { ".webp", ".jpg", ".jpeg", ".png", ".gif" };

    /// <summary>ساخت نام. <paramref name="random"/> فقط برای آزمون (پیش‌فرض: ۴ بایت تصادفی امن).</summary>
    public static string? Build(string? slug, string? extension, string? random = null)
    {
        extension = extension?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(slug) || extension is null || !Extensions.Contains(extension))
            return null;
        var clean = new string(slug.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        clean = string.Join('-', clean.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length > MaxSlugLength)
        {
            clean = clean[..MaxSlugLength];
            var cut = clean.LastIndexOf('-');
            if (cut >= 20)
                clean = clean[..cut];
            clean = clean.TrimEnd('-');
        }
        if (clean.Length < 3 || !clean.Any(char.IsAsciiLetter))
            return null;
        random ??= Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        return $"{clean}-{random}{extension}";
    }
}
