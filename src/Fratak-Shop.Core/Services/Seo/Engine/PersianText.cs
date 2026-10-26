using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  ابزارهای متن فارسی برای موتور سئو (بدون هیچ کتابخانه یا سرویس خارجی)
//  • نرمال‌سازی (ي/ك عربی، اعراب، کشیده، ارقام)، شکستن متن به واژه و جمله، ریشه‌یابی سبک،
//    واژه‌های ایست (حرف اضافه و ربط)، تشخیص خط فارسی/لاتین، تبدیل HTML به متن و آمار ساختار.
//  • همهٔ اجزای موتور (تحلیل‌گر، برنامه‌ریز کلمهٔ کلیدی، نویسنده، تحقیق رقبا) از این کلاس استفاده می‌کنند
//    تا «گوشی‌های سامسونگ» و «گوشي هاي سامسونگ» یکی حساب شوند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>ابزارهای پایهٔ متن فارسی (ایستا و بدون وضعیت؛ امن برای استفادهٔ هم‌زمان).</summary>
public static partial class PersianText
{
    /// <summary>نیم‌فاصله (ZWNJ).</summary>
    public const char Zwnj = '\u200C';

    /// <summary>
    /// نرمال‌سازی برای «مقایسه» (نه نمایش): حروف عربی → فارسی، حذف اعراب و کشیده، ارقام → انگلیسی، حروف کوچک،
    /// نیم‌فاصله → فاصله، فاصله‌های تکراری حذف.
    /// مثال: «گوشيِ‌ هاي  ۵G» → «گوشی های 5g».
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        var lastSpace = true;
        foreach (var raw in text)
        {
            // اعراب (فتحه، کسره، تشدید …) و کشیده هیچ معنایی در جست‌وجو ندارند.
            if (raw is >= '\u064B' and <= '\u065F' or '\u0670' or '\u0640')
                continue;

            var ch = raw switch
            {
                'ي' or 'ى' or 'ئ' => 'ی',
                'ك' => 'ک',
                'ة' or 'ۀ' => 'ه',
                'أ' or 'إ' or 'ٱ' => 'ا',
                'ؤ' => 'و',
                >= '۰' and <= '۹' => (char)('0' + (raw - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (raw - '٠')),
                Zwnj or '\u200F' or '\u200E' or '\u00A0' => ' ',
                _ => char.ToLowerInvariant(raw),
            };

            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                if (!lastSpace)
                    builder.Append(' ');
                lastSpace = true;
                continue;
            }

            builder.Append(ch);
            lastSpace = false;
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// واژه‌های متن (پس از نرمال‌سازی). فقط حرف و رقم؛ علامت‌ها جداکننده‌اند.
    /// «A55» و «256GB» یک واژه می‌مانند. مثال: «گوشی سامسونگ، مدل A55!» → [گوشی، سامسونگ، مدل، a55].
    /// </summary>
    public static List<string> Tokenize(string? text)
    {
        var normalized = Normalize(text);
        var tokens = new List<string>();
        if (normalized.Length == 0)
            return tokens;

        var start = -1;
        for (var i = 0; i <= normalized.Length; i++)
        {
            var isWordChar = i < normalized.Length && (char.IsLetterOrDigit(normalized[i]) || normalized[i] == '.' && IsDecimalPoint(normalized, i));
            if (isWordChar && start < 0)
                start = i;
            else if (!isWordChar && start >= 0)
            {
                tokens.Add(normalized[start..i]);
                start = -1;
            }
        }

        return tokens;
    }

    // «6.5» یک عدد است (نقطه بین دو رقم)، ولی نقطهٔ آخر جمله جداکننده است.
    private static bool IsDecimalPoint(string text, int index) =>
        index > 0 && index < text.Length - 1 && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]);

    /// <summary>تعداد واژه‌های متن ساده.</summary>
    public static int WordCount(string? text) => Tokenize(text).Count;

    /// <summary>
    /// واژه‌های ایست: حرف اضافه، ربط، فعل‌های کمکی و … که در کلمهٔ کلیدی و شمارش واژه‌های مهم حساب نمی‌شوند.
    /// (فهرست بر اساس واژه‌های پرتکرار فارسی و چند واژهٔ انگلیسی رایج.)
    /// </summary>
    public static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "و", "در", "به", "از", "که", "این", "آن", "با", "را", "برای", "است", "هست", "نیست", "می", "تا", "یا", "هم", "نیز",
        "بر", "اما", "ولی", "اگر", "چه", "چرا", "چگونه", "های", "ها", "یک", "شود", "میشود", "کند", "میکند", "شد", "بود", "باشد", "دارد",
        "دارند", "کرد", "کرده", "شده", "خود", "او", "ما", "شما", "آنها", "ایشان", "من", "تو", "هر", "همه", "دیگر", "پس", "بی", "بین",
        "روی", "زیر", "بالای", "پیش", "نزد", "مانند", "مثل", "چون", "چنین", "چنان", "همین", "همان", "اینکه", "آنکه", "وی", "ای", "اینها",
        "باید", "نباید", "بسیار", "خیلی", "کاملا", "فقط", "حتی", "سپس", "یعنی", "ـ", "شوند", "کنند", "کنید", "کنیم", "بودن", "داشت",
        "داشته", "نمی", "ندارد", "هستند", "بوده", "توسط", "طی", "ضمن", "درباره", "دربارهٔ", "کدام", "چند", "چقدر", "آیا", "بله", "خیر",
        "the", "and", "of", "for", "with", "a", "an", "to", "in", "on", "by", "is", "are", "or", "at", "from", "as", "be", "it", "this",
    };

    /// <summary>واژهٔ ایست یا خیلی کوتاه (یک حرفی) است؟ ارقام هرگز ایست نیستند (مدل «A55» یا «256» مهم‌اند).</summary>
    public static bool IsStopWord(string token) =>
        token.Length == 0 || StopWords.Contains(token) || token.Length == 1 && !char.IsDigit(token[0]);

    /// <summary>
    /// ریشه‌یابی سبک فارسی: فقط پسوندهای جمع و مالکیت رایج حذف می‌شود تا «گوشی‌ها»، «گوشیهای» و «گوشی» یکی شوند.
    /// عمداً محافظه‌کار است (واژه‌های کوتاه دست نمی‌خورند) تا معنای واژه عوض نشود.
    /// مثال: «هدفون‌های» → «هدفون»، «کتابها» → «کتاب»، «مشخصات» → «مشخص». «بهترین» دست نمی‌خورد (بعد از حذف کمتر از ۳ حرف می‌ماند)
    /// و «تر» عمداً حذف نمی‌شود چون «کامپیوتر» را خراب می‌کند.
    /// </summary>
    public static string Stem(string token)
    {
        if (token.Length <= 3 || !IsPersianLetter(token[0]))
            return token;

        // ترتیب مهم است: پسوندهای بلندتر اول.
        foreach (var suffix in StemSuffixes)
        {
            if (token.Length - suffix.Length >= 3 && token.EndsWith(suffix, StringComparison.Ordinal))
                return token[..^suffix.Length];
        }

        return token;
    }

    private static readonly string[] StemSuffixes = ["هایی", "هایم", "هایت", "هایش", "های", "ها", "ترین", "ات"];

    /// <summary>ریشهٔ همهٔ واژه‌های غیرایست. برای مقایسهٔ کلمهٔ کلیدی با متن.</summary>
    public static List<string> ContentStems(string? text) =>
        Tokenize(text).Where(t => !IsStopWord(t)).Select(Stem).ToList();

    /// <summary>حرف فارسی/عربی است؟</summary>
    public static bool IsPersianLetter(char ch) => ch is >= '\u0600' and <= '\u06FF' or >= '\uFB50' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF';

    /// <summary>
    /// سهم حروف فارسی از کل حروف (۰ تا ۱). برای تشخیص خط نامک یا کلمهٔ کلیدی.
    /// مثال: «samsung-a55» → ۰، «گوشی-سامسونگ» → ۱.
    /// </summary>
    public static double PersianRatio(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        int persian = 0, letters = 0;
        foreach (var ch in text)
        {
            if (!char.IsLetter(ch))
                continue;
            letters++;
            if (IsPersianLetter(ch))
                persian++;
        }
        return letters == 0 ? 0 : (double)persian / letters;
    }

    /// <summary>
    /// تقسیم متن به جمله‌ها (نقطه، علامت سؤال فارسی/انگلیسی، تعجب، نقطه‌ویرگول و خط جدید).
    /// نقطهٔ اعشار («۶.۵ اینچ») جمله را نمی‌شکند.
    /// </summary>
    public static List<string> Sentences(string? text)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return list;

        var builder = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var isEnd = ch is '!' or '؟' or '?' or '؛' or '\n' or '…'
                        || ch == '.' && !(i > 0 && i < text.Length - 1 && char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1]));
            if (ch != '\n')
                builder.Append(ch);
            if (isEnd)
            {
                Flush();
            }
        }
        Flush();
        return list;

        void Flush()
        {
            var sentence = builder.ToString().Trim();
            if (sentence.Length > 0 && sentence.Any(char.IsLetterOrDigit))
                list.Add(sentence);
            builder.Clear();
        }
    }

    /// <summary>
    /// متن خوانا برای کاربر: فاصله‌های اضافه حذف، و حروف عربی یکسان می‌شوند (ارقام و نیم‌فاصله دست نمی‌خورند).
    /// برای متن‌هایی که موتور می‌نویسد و نمایش داده می‌شود.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var fixedLetters = text.Replace('ي', 'ی').Replace('ك', 'ک').Replace('\u00A0', ' ');
        return MultiSpace().Replace(fixedLetters, " ").Trim();
    }

    /// <summary>کوتاه کردن متن بدون شکستن واژه (با «…» در انتها). مثال: Truncate("یک متن بلند", 8) → «یک متن…».</summary>
    public static string Truncate(string text, int max)
    {
        text = Clean(text);
        if (text.Length <= max)
            return text;
        var cut = text[..max];
        var space = cut.LastIndexOf(' ');
        if (space > max / 2)
            cut = cut[..space];
        return cut.TrimEnd('،', ',', '؛', ';', ':', '-', '–', ' ') + "…";
    }

    /// <summary>هش کوتاه (۳۲ رقم هگز) برای فهمیدن تغییر محتوا.</summary>
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0, 16).ToLowerInvariant();

    /// <summary>
    /// عدد به رقم فارسی (برای متن‌های تولیدی). مثال: 7 → «۷».
    /// </summary>
    public static string Digits(long value) =>
        new(value.ToString(CultureInfo.InvariantCulture).Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray());

    /// <summary>عدد اعشاری با یک رقم اعشار و ممیز فارسی (برای جایگاه میانگین گوگل). مثال: 7.46 → «۷٫۵»، 3.0 → «۳».</summary>
    public static string Decimal1(double value)
    {
        var text = Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture).Replace('.', '٫');
        return new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray());
    }

    /// <summary>مبلغ با جداکنندهٔ هزارگان و رقم فارسی. مثال: 1500000 → «۱٬۵۰۰٬۰۰۰».</summary>
    public static string Money(decimal value)
    {
        var text = Math.Round(value).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '٬');
        return new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray());
    }

    /// <summary>
    /// فهرست فارسی با «،» و «و». مثال: [الف، ب، ج] → «الف، ب و ج».
    /// </summary>
    public static string JoinList(IReadOnlyList<string> items)
    {
        if (items.Count == 0) return string.Empty;
        if (items.Count == 1) return items[0];
        return string.Join("، ", items.Take(items.Count - 1)) + " و " + items[^1];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpace();

    // ── HTML ───────────────────────────────────────────────────────────

    /// <summary>
    /// تحلیل ساختار یک HTML (توضیحات محصول یا متن مقاله) با AngleSharp: متن ساده، پاراگراف‌ها، تیترها، تصویرها و لینک‌ها.
    /// اسکریپت و استایل نادیده گرفته می‌شوند. HTML خالی → آمار خالی.
    /// </summary>
    public static HtmlStats AnalyzeHtml(string? html)
    {
        var stats = new HtmlStats();
        if (string.IsNullOrWhiteSpace(html))
            return stats;

        var document = new HtmlParser().ParseDocument("<html><body>" + html + "</body></html>");
        var body = document.Body;
        if (body is null)
            return stats;

        foreach (var junk in body.QuerySelectorAll("script,style,noscript,template").ToList())
            junk.Remove();

        foreach (var heading in body.QuerySelectorAll("h1,h2,h3,h4"))
        {
            var text = Clean(heading.TextContent);
            if (text.Length > 0)
                stats.Headings.Add(new HtmlHeading(heading.LocalName[1] - '0', text));
        }

        foreach (var image in body.QuerySelectorAll("img"))
            stats.Images.Add(new HtmlImage(image.GetAttribute("src") ?? "", image.GetAttribute("alt")));

        foreach (var link in body.QuerySelectorAll("a[href]"))
        {
            var href = link.GetAttribute("href") ?? "";
            stats.Links.Add(new HtmlLink(href, Clean(link.TextContent), IsInternal(href)));
        }

        // پاراگراف = هر بلوک متنی (p، li، td، blockquote …). اگر HTML فقط متن خام باشد، خطوط پاراگرافند.
        foreach (var block in body.QuerySelectorAll("p,li,blockquote,td,dd,figcaption"))
        {
            if (block.QuerySelector("p,li") is not null)
                continue; // بلوک تودرتو: پاراگراف‌های داخلی جداگانه شمرده می‌شوند.
            var text = Clean(block.TextContent);
            if (text.Length > 0)
                stats.Paragraphs.Add(text);
        }

        var plain = BlockText(body);
        stats.Text = plain;
        if (stats.Paragraphs.Count == 0)
            stats.Paragraphs.AddRange(plain.Split('\n').Select(Clean).Where(p => p.Length > 0));
        stats.Words = WordCount(plain);
        return stats;
    }

    /// <summary>HTML → متن ساده (بلوک‌ها با خط جدید جدا می‌شوند تا جمله‌ها به هم نچسبند).</summary>
    public static string HtmlToText(string? html) => AnalyzeHtml(html).Text;

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "li", "ul", "ol", "h1", "h2", "h3", "h4", "h5", "h6", "br", "tr", "table", "blockquote", "section", "article", "figcaption", "dd", "dt",
    };

    private static string BlockText(IElement root)
    {
        var builder = new StringBuilder();
        Walk(root);
        return string.Join('\n', builder.ToString().Split('\n').Select(Clean).Where(l => l.Length > 0));

        void Walk(INode node)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child is IText text)
                    builder.Append(text.Data);
                else if (child is IElement element)
                {
                    var block = BlockTags.Contains(element.LocalName);
                    if (block) builder.Append('\n');
                    Walk(element);
                    if (block) builder.Append('\n');
                }
            }
        }
    }

    /// <summary>لینک داخلی سایت است؟ (آدرس نسبی مثل «/product/...»؛ لینک کامل خارجی، ایمیل و تلفن داخلی نیستند).</summary>
    public static bool IsInternal(string href) =>
        href.StartsWith('/') && !href.StartsWith("//", StringComparison.Ordinal) || href.StartsWith('#');
}

/// <summary>تیتر داخل متن. Level: ۱ تا ۴ (h1 تا h4).</summary>
public sealed record HtmlHeading(int Level, string Text);

/// <summary>تصویر داخل متن با متن جایگزین (alt).</summary>
public sealed record HtmlImage(string Src, string? Alt);

/// <summary>لینک داخل متن.</summary>
public sealed record HtmlLink(string Href, string Text, bool Internal);

/// <summary>آمار ساختاری یک HTML (خروجی <see cref="PersianText.AnalyzeHtml"/>).</summary>
public sealed class HtmlStats
{
    public string Text { get; set; } = string.Empty;
    public int Words { get; set; }
    public List<string> Paragraphs { get; } = [];
    public List<HtmlHeading> Headings { get; } = [];
    public List<HtmlImage> Images { get; } = [];
    public List<HtmlLink> Links { get; } = [];
}
