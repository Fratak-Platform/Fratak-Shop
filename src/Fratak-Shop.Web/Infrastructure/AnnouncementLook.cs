using System.Text.RegularExpressions;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// ظاهر نهایی «نوار اطلاعیهٔ بالای سایت» (دور ۲۱) که از تنظیمات ساخته می‌شود.
/// <para>همهٔ مقدارها امن‌اند: رنگ‌ها فقط هگز معتبر، کلاس‌ها فقط از فهرست ثابت، متن‌ها کوتاه و بدون خط خالی.
/// پس هیچ مقدار دلخواهی از تنظیمات مستقیم وارد CSS یا HTML نمی‌شود (جلوگیری از تزریق).</para>
/// <para>مثال: سبک «جشن» + جلوهٔ «درخشش» + اندازهٔ «بزرگ» ← Background = گرادیان رنگارنگ متحرک،
/// CssClass = «fs-ann fs-ann-festive fs-ann-shimmer text-base font-bold»، HeightClass = «h-11».</para>
/// <para>محل استفاده: SiteContext.Announcement ← Header/Default.cshtml (خود نوار) و _Layout.cshtml (فاصلهٔ زیر هدر ثابت).</para>
/// </summary>
public sealed record AnnouncementLook(
    string Background,
    string TextColor,
    string CssClass,
    string HeightClass,
    IReadOnlyList<string> Messages,
    bool Closeable)
{
    /// <summary>سبک‌های نوار. عدد همان مقدار ذخیره‌شده در تنظیمات است.</summary>
    public static readonly (int Value, string Title)[] Styles =
    [
        (0, "ساده (یک رنگ)"), (1, "گرادیان دو رنگ"), (2, "جشن و رنگارنگ (متحرک)"), (3, "فوری و هشدار"), (4, "طلایی و ویژه"),
    ];

    /// <summary>جلوه‌های جلب توجه. همه با «کاهش حرکت» سیستم‌عامل خاموش می‌شوند.</summary>
    public static readonly (int Value, string Title)[] Effects =
    [
        (0, "بدون جلوه"), (1, "چشمک ملایم"), (2, "تپش"), (3, "درخشش"), (4, "متن متحرک (حرکت افقی)"),
    ];

    public static readonly (int Value, string Title)[] Sizes = [(0, "کوچک"), (1, "متوسط"), (2, "بزرگ")];

    public static readonly (int Value, string Title)[] Weights = [(0, "معمولی"), (1, "پررنگ"), (2, "خیلی پررنگ")];

    /// <summary>بیشترین تعداد پیام چرخشی (پیام اصلی + خط‌های اضافه).</summary>
    public const int MaxMessages = 6;

    /// <summary>
    /// ساخت ظاهر از مقدارهای خام تنظیمات. ورودی نامعتبر ← پیش‌فرض امن (هرگز استثنا نمی‌دهد).
    /// <para>مثال: Build("#0B1927", null, null, 0, 0, 0, 0, "ارسال رایگان", null, false) ← نوار ساده سرمه‌ای با متن سفید.</para>
    /// </summary>
    public static AnnouncementLook Build(
        string? color, string? color2, string? textColor, int style, int effect, int size, int weight,
        string? text, string? extraMessages, bool closeable, string fallbackColor = BrandIdentity.DefaultAnnouncementColor)
    {
        var c1 = SafeColor(color) ?? fallbackColor;
        var c2 = SafeColor(color2);
        var (background, defaultText, styleClass) = style switch
        {
            1 => ($"linear-gradient(90deg, {c1}, {c2 ?? Shift(c1)})", "#ffffff", "fs-ann-gradient"),
            2 => ("linear-gradient(270deg, #ef476f, #f78c6b, #ffd166, #06d6a0, #118ab2, #8338ec, #ef476f)", "#ffffff", "fs-ann-festive"),
            3 => ($"linear-gradient(90deg, #b91c1c, {c2 ?? "#ea580c"})", "#ffffff", "fs-ann-urgent"),
            4 => ("linear-gradient(90deg, #6b4e16, #c9a227, #f5d77a, #c9a227, #6b4e16)", "#1f1500", "fs-ann-gold"),
            _ => (c1, "#ffffff", "fs-ann-solid"),
        };
        var effectClass = effect switch
        {
            1 => " fs-ann-blink",
            2 => " fs-ann-pulse",
            3 => " fs-ann-shimmer",
            4 => " fs-ann-marquee",
            _ => "",
        };
        var sizeClass = size switch { 1 => " text-sm", 2 => " text-base", _ => " text-xs md:text-sm" };
        var weightClass = weight switch { 1 => " font-bold", 2 => " font-black", _ => "" };
        var messages = ParseMessages(text, extraMessages);
        return new AnnouncementLook(
            background,
            SafeColor(textColor) ?? defaultText,
            "fs-ann " + styleClass + effectClass + sizeClass + weightClass,
            size == 2 ? "h-11" : "h-9",
            messages,
            closeable);
    }

    /// <summary>
    /// پیام‌های نوار: متن اصلی + هر خط غیرخالی از «پیام‌های چرخشی» (تکراری حذف، هر پیام ≤ ۲۰۰ نویسه، حداکثر ۶).
    /// مثال: («ارسال رایگان»، «تخفیف ۱۰٪\n\nارسال رایگان») ← [«ارسال رایگان»، «تخفیف ۱۰٪»].
    /// </summary>
    public static IReadOnlyList<string> ParseMessages(string? text, string? extra)
    {
        var list = new List<string>();
        foreach (var line in new[] { text }.Concat((extra ?? "").Split('\n')))
        {
            var clean = Regex.Replace(line ?? "", @"\s+", " ").Trim();
            if (clean.Length == 0)
                continue;
            if (clean.Length > 200)
                clean = clean[..200];
            if (!list.Contains(clean, StringComparer.Ordinal))
                list.Add(clean);
            if (list.Count >= MaxMessages)
                break;
        }
        return list;
    }

    /// <summary>فقط رنگ هگز (#abc یا #aabbcc)؛ بقیه ← null. مثال: «red» یا «#fff;background:url(x)» ← null.</summary>
    public static string? SafeColor(string? value) =>
        value is not null && Regex.IsMatch(value.Trim(), "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$") ? value.Trim() : null;

    /// <summary>رنگ دوم پیش‌فرض گرادیان: همان رنگ کمی روشن‌تر. مثال: «#0B1927» ← «#3d4b59».</summary>
    public static string Shift(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 3)
            h = string.Concat(h.Select(ch => new string(ch, 2)));
        if (h.Length != 6)
            return hex;
        var r = Convert.ToInt32(h[..2], 16);
        var g = Convert.ToInt32(h[2..4], 16);
        var b = Convert.ToInt32(h[4..], 16);
        static int Up(int v) => Math.Min(255, v + 50);
        return $"#{Up(r):x2}{Up(g):x2}{Up(b):x2}";
    }
}
