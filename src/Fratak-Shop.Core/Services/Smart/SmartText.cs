using System.Numerics;
using System.Text;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// ابزارهای متنی خالص (بدون دیتابیس و شبکه) برای امکانات هوشمند؛ همه قطعی‌اند و آزمون واحد دارند.
/// <list type="bullet">
/// <item><see cref="Shingles"/> و <see cref="CopiedRatio"/>: محافظ کپی (B13) – متن تولیدی نباید ۶ کلمهٔ پشت‌سرهم از منبعی کپی کند.</item>
/// <item><see cref="Jaccard"/> و <see cref="Cluster"/>: گروه‌بندی عبارت‌های هم‌معنی نظرها (B11) – «باتری قوی» و «باتریش قویه».</item>
/// <item><see cref="ShortPhrase"/>: کوتاه‌کردن عبارت به حداکثر ۵ کلمهٔ معنادار.</item>
/// <item><see cref="Pick"/>: انتخاب قطعی یک جمله از چند جملهٔ هم‌معنی (برای متن‌های متنوع و غیرتکراری).</item>
/// </list>
/// </summary>
public static class SmartText
{
    /// <summary>
    /// مجموعهٔ «n‌کلمه‌ای‌های پشت‌سرهم» متن یکسان‌شده. مثال (n=3): «این گوشی باتری قوی دارد» ←
    /// {«این گوشی باتری»، «گوشی باتری قوی»، «باتری قوی دارد»}.
    /// </summary>
    public static HashSet<string> Shingles(string? text, int n = 6)
    {
        var tokens = PersianText.Tokenize(text);
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + n <= tokens.Count; i++)
            set.Add(string.Join(' ', tokens.Skip(i).Take(n)));
        return set;
    }

    /// <summary>
    /// چه سهمی از n‌کلمه‌ای‌های متن تولیدی، عیناً در یکی از منبع‌ها آمده است (۰ تا ۱).
    /// مثال: متنی که یک جملهٔ ۸ کلمه‌ای را عیناً از نظر کاربر برداشته ← بزرگ‌تر از صفر ← رد می‌شود.
    /// </summary>
    public static double CopiedRatio(string? generated, IEnumerable<string?> sources, int n = 6)
    {
        var mine = Shingles(generated, n);
        if (mine.Count == 0)
            return 0;
        var theirs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
            theirs.UnionWith(Shingles(source, n));
        if (theirs.Count == 0)
            return 0;
        var copied = mine.Count(theirs.Contains);
        return (double)copied / mine.Count;
    }

    /// <summary>آیا دست‌کم یک تکهٔ n کلمه‌ای عیناً از منبع آمده است؟ (قانون سخت‌گیرانهٔ B13)</summary>
    public static bool IsCopied(string? generated, IEnumerable<string?> sources, int n = 6) => CopiedRatio(generated, sources, n) > 0;

    /// <summary>
    /// شباهت ژاکارد ریشه‌های معنادار دو عبارت (۰ تا ۱). مثال: «باتری قوی» و «باتریش خیلی قویه» ← نزدیک ۱.
    /// </summary>
    public static double Jaccard(string? a, string? b)
    {
        var x = Stems(a);
        var y = Stems(b);
        if (x.Count == 0 || y.Count == 0)
            return 0;
        var inter = x.Count(y.Contains);
        return (double)inter / (x.Count + y.Count - inter);
    }

    /// <summary>ریشهٔ کلمه‌های معنادار (بدون کلمه‌های ایست و پسوندهای محاوره‌ای «ـش، ـه، ـیه»).</summary>
    public static HashSet<string> Stems(string? text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in PersianText.Tokenize(text))
        {
            if (PersianText.IsStopWord(token) || Intensifiers.Contains(token) || token.Length < 2)
                continue;
            set.Add(Colloquial(PersianText.Stem(token)));
        }
        return set;
    }

    /// <summary>کلمه‌های شدت («خیلی، واقعا، بسیار») که معنای ویژگی را عوض نمی‌کنند.</summary>
    public static readonly HashSet<string> Intensifiers = new(StringComparer.Ordinal)
    {
        "خیلی", "واقعا", "واقعاً", "بسیار", "کاملا", "کاملاً", "فوق", "العاده", "نسبتا", "نسبتاً", "یکم", "کمی", "یه", "یک", "هم", "دیگه", "اصلا", "اصلاً", "بی", "نظیر",
    };

    private static string Colloquial(string stem)
    {
        // «قویه» ← «قوی»؛ «باتریش» ← «باتری»؛ «صداش» ← «صدا»
        if (stem.Length > 3 && (stem.EndsWith('ه') || stem.EndsWith('ش')))
            return stem[..^1];
        return stem;
    }

    /// <summary>
    /// خوشه‌بندی حریصانه: هر عبارت به نخستین خوشه‌ای که شباهتش با «نمایندهٔ» آن ≥ آستانه است می‌پیوندد.
    /// خروجی به ترتیب تعداد اعضا (پرتکرارترین اول). نماینده = کوتاه‌ترین عبارت پرتکرار خوشه.
    /// مثال: [«باتری قوی»، «باتریش قویه»، «صدای بلند»] ← [(«باتری قوی»، ۲)، («صدای بلند»، ۱)].
    /// </summary>
    public static List<PhraseCluster> Cluster(IEnumerable<string> phrases, double threshold = 0.5)
    {
        var clusters = new List<(List<string> Members, HashSet<string> Stems)>();
        foreach (var raw in phrases)
        {
            var phrase = PersianText.Clean(raw);
            var stems = Stems(phrase);
            if (stems.Count == 0)
                continue;
            var placed = false;
            foreach (var cluster in clusters)
            {
                var inter = cluster.Stems.Count(stems.Contains);
                var score = (double)inter / (cluster.Stems.Count + stems.Count - inter);
                if (score >= threshold || inter > 0 && (inter == stems.Count || inter == cluster.Stems.Count))
                {
                    cluster.Members.Add(phrase);
                    placed = true;
                    break;
                }
            }
            if (!placed)
                clusters.Add(([phrase], stems));
        }
        return clusters
            .Select(c => new PhraseCluster(Representative(c.Members), c.Members.Count, c.Members))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Label.Length).ToList();
    }

    private static string Representative(List<string> members) =>
        members.GroupBy(m => m, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Length).First().Key;

    /// <summary>
    /// کوتاه‌کردن عبارت به حداکثر <paramref name="maxWords"/> کلمه، بدون علامت‌ها و شکلک‌ها.
    /// مثال: «کیفیت ساختش عالیه!!! 😍 واقعا راضیم» ← «کیفیت ساختش عالیه واقعا راضیم».
    /// </summary>
    public static string ShortPhrase(string? text, int maxWords = 5)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
            builder.Append(char.IsLetterOrDigit(ch) || ch == '\u200c' || ch == '.' ? ch : ' ');
        var words = builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(maxWords);
        return PersianText.Clean(string.Join(' ', words)).Trim('.');
    }

    /// <summary>
    /// انتخاب قطعی یکی از گزینه‌ها بر اساس «بذر» (مثلاً شناسهٔ محصول). هر محصول همیشه همان جمله را می‌گیرد،
    /// ولی محصولات مختلف جمله‌های متفاوت ← متن تکراری در سایت ساخته نمی‌شود.
    /// </summary>
    public static string Pick(string seed, params string[] options)
    {
        if (options.Length == 0)
            return string.Empty;
        var hash = 2166136261u;
        foreach (var ch in seed)
            hash = (hash ^ ch) * 16777619u;
        return options[(int)(hash % (uint)options.Length)];
    }

    /// <summary>میانگین تعداد کلمهٔ جمله‌ها (برای دروازهٔ کیفیت؛ هدف ≤ ۲۵).</summary>
    public static double AverageSentenceWords(string? text)
    {
        var sentences = PersianText.Sentences(text);
        return sentences.Count == 0 ? 0 : sentences.Average(s => PersianText.WordCount(s));
    }
}

/// <summary>یک خوشهٔ عبارت هم‌معنی: برچسب نماینده، تعداد تکرار و همهٔ عبارت‌ها.</summary>
public sealed record PhraseCluster(string Label, int Count, IReadOnlyList<string> Members);

/// <summary>
/// اصلاح «زبان کیبورد اشتباه» در جست‌وجو (C15): کاربر فراموش کرده زبان کیبورد را عوض کند.
/// مثال‌ها: «hdk» ← «این»؛ «sdl» ← «سیم»؛ «لشمشطغ» ← «galaxy».
/// <para>محل استفاده: صفحهٔ جست‌وجو وقتی نتیجه‌ای پیدا نشد (پیشنهاد «منظورتان … بود؟»).</para>
/// </summary>
public static class KeyboardLayout
{
    // چیدمان استاندارد کیبورد فارسی (ISIRI 9147) روی کلیدهای QWERTY.
    private const string Latin = "qwertyuiop[]asdfghjkl;'zxcvbnm,";
    private const string Persian = "ضصثقفغعهخحجچشسیبلاتنمکگظطزرذدپو";

    private static readonly Dictionary<char, char> LatinToPersian = Build(Latin, Persian);
    private static readonly Dictionary<char, char> PersianToLatin = Build(Persian, Latin);

    private static Dictionary<char, char> Build(string from, string to)
    {
        var map = new Dictionary<char, char>();
        for (var i = 0; i < Math.Min(from.Length, to.Length); i++)
            map.TryAdd(from[i], to[i]);
        return map;
    }

    /// <summary>
    /// اگر متن ظاهراً با کیبورد اشتباه تایپ شده، نسخهٔ اصلاح‌شده را برمی‌گرداند؛ وگرنه null.
    /// شرط: دست‌کم ۲ حرف، و همهٔ حروف فقط از یک الفبا باشند (متن مخلوط دست نمی‌خورد).
    /// </summary>
    public static string? Swap(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 100)
            return null;
        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count < 2)
            return null;
        var allLatin = letters.All(c => c < 128);
        var allPersian = letters.All(PersianText.IsPersianLetter);
        if (allLatin == allPersian)
            return null;
        var map = allLatin ? LatinToPersian : PersianToLatin;
        var builder = new StringBuilder(text.Length);
        foreach (var ch in allLatin ? text.ToLowerInvariant() : text)
            builder.Append(map.TryGetValue(ch == 'ي' ? 'ی' : ch == 'ك' ? 'ک' : ch, out var mapped) ? mapped : ch);
        var swapped = builder.ToString();
        return string.Equals(swapped, text, StringComparison.Ordinal) ? null : swapped;
    }
}

/// <summary>
/// هش ادراکی تصویر (dHash) – مقایسهٔ تکراری بودن عکس‌ها (B3). خود محاسبهٔ هش از پیکسل‌ها در لایهٔ وب (Skia) انجام می‌شود؛
/// این کلاس فقط منطق خالص را دارد تا آزمون‌پذیر باشد.
/// </summary>
public static class ImageHash
{
    /// <summary>
    /// dHash از یک تصویر خاکستری ۹×۸: هر بیت = «پیکسل چپ روشن‌تر از راست است؟». ورودی باید دقیقاً ۷۲ مقدار باشد.
    /// </summary>
    public static ulong DHash(ReadOnlySpan<byte> gray9x8)
    {
        if (gray9x8.Length != 72)
            throw new ArgumentException("ورودی dHash باید ۷۲ پیکسل (۹×۸) باشد.", nameof(gray9x8));
        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++, bit++)
        {
            if (gray9x8[y * 9 + x] > gray9x8[y * 9 + x + 1])
                hash |= 1UL << bit;
        }
        return hash;
    }

    /// <summary>فاصلهٔ همینگ دو هش (۰ = یکسان، ≤ ۶ = تقریباً همان عکس با اندازه/فشرده‌سازی متفاوت).</summary>
    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    /// <summary>آستانهٔ «همان عکس» (B3).</summary>
    public const int DuplicateDistance = 6;

    public static bool IsDuplicate(ulong a, ulong b) => Distance(a, b) <= DuplicateDistance;
}
