namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// دور ۲۸: نتیجهٔ «بررسی سوژهٔ عکس» – آیا عکس خود کالا/سوژه را (ترجیحاً روی زمینهٔ سفید) نشان می‌دهد؟
/// </summary>
/// <param name="WhiteBackground">حاشیهٔ عکس تقریباً سفید است (عکس کاتالوگی استاندارد).</param>
/// <param name="PlainBackground">حاشیه یکدست است (سفید، خاکستری یا هر رنگ ساده) – یعنی صحنهٔ شلوغ نیست.</param>
/// <param name="Coverage">سهم پیکسل‌های سوژه از کل عکس (۰ تا ۱). خیلی کم = سوژه ریز است؛ ۱ = تمام‌قاب.</param>
/// <param name="Centered">مرکز سوژه در میانهٔ عکس است.</param>
/// <param name="Skin">سهم پیکسل‌های «رنگ پوست» در سوژه (نشانهٔ حضور آدم).</param>
/// <param name="People">احتمال زیاد آدم/دست/صورت در عکس.</param>
/// <param name="Lifestyle">عکس صحنه/فضاسازی است (زمینهٔ شلوغ)، نه عکس خود کالا.</param>
/// <param name="Watermark">نشانهٔ لوگو/واترمارک در گوشه‌ها یا سایهٔ کم‌رنگ متن روی زمینه.</param>
/// <param name="Confidence">اطمینان کلی ۰ تا ۱ که «این عکس مناسب نمایش این کالاست».</param>
/// <param name="Accept">قبول/رد نهایی (رد = جست‌وجو برای عکس دیگر ادامه می‌یابد).</param>
/// <param name="Reasons">دلیل‌های فارسی کوتاه برای پنل («زمینهٔ سفید»، «احتمال حضور آدم» …).</param>
public sealed record SubjectVerdict(
    bool WhiteBackground,
    bool PlainBackground,
    double Coverage,
    bool Centered,
    double Skin,
    bool People,
    bool Lifestyle,
    bool Watermark,
    double Confidence,
    bool Accept,
    IReadOnlyList<string> Reasons);

/// <summary>
/// دور ۲۸ (#۳۶ تا #۵۰) – موتور داخلی «اعتبارسنجی عکس» بدون هیچ سرویس یا مدل بیرونی (فقط حساب روی پیکسل‌ها).
/// <para>
/// ورودی: پیکسل‌های RGB یک نسخهٔ کوچک (مثلاً حداکثر ۱۲۸ پیکسل) – ۳ بایت برای هر پیکسل، ردیف به ردیف.
/// لایهٔ وب (SkiaSharp) عکس را باز و کوچک می‌کند و این کلاس فقط حساب می‌کند تا آزمون واحد ساده باشد.
/// </para>
/// <para>مراحل:</para>
/// <list type="number">
/// <item>حاشیهٔ عکس (۶٪ هر لبه) خوانده می‌شود: چند درصد سفید است و چند درصد هم‌رنگ میانهٔ حاشیه (یکدست بودن).</item>
/// <item>اگر زمینه یکدست است، هر پیکسلی که با رنگ زمینه فاصلهٔ زیادی دارد «سوژه» است ← سهم سوژه، کادر سوژه و مرکز آن.</item>
/// <item>رنگ پوست (قاعدهٔ YCbCr + RGB) در پیکسل‌های سوژه شمرده می‌شود ← احتمال آدم.</item>
/// <item>گوشه‌ها: لکهٔ کوچک جدا از سوژه در یک یا دو گوشه ← لوگو/واترمارک؛ «سایهٔ کم‌رنگ» زیاد روی زمینهٔ سفید ← واترمارک شفاف.</item>
/// <item>امتیاز اطمینان از همهٔ این‌ها + کیفیت عکس + تطابق عنوان منبع ساخته می‌شود.</item>
/// </list>
/// <para>محل استفاده: SmartImageFinder (عکس کالا و دسته)، صفحهٔ «سلامت داده» (بازبینی عکس‌های موجود) و آزمون‌ها.</para>
/// </summary>
public static class ImageSubjectCheck
{
    /// <summary>کمترین اطمینان برای «پذیرش» در حالت عادی.</summary>
    public const double MinConfidence = 0.45;

    /// <summary>کمترین اطمینان برای اینکه عکس کالای پیش‌نویس بدون انتظار تأیید کارشناس اضافه شود (منبع مطمئن هم لازم است).</summary>
    public const double AutoAttachConfidence = 0.72;

    /// <summary>
    /// بررسی پیکسل‌ها.
    /// </summary>
    /// <param name="rgb">پیکسل‌های RGB (۳ بایت برای هر پیکسل).</param>
    /// <param name="width">عرض.</param>
    /// <param name="height">ارتفاع.</param>
    /// <param name="match">تطابق عنوان/منبع با کالا (۰ تا ۱). نامعلوم ← ۰٫۵.</param>
    /// <param name="quality">امتیاز کیفیت ۰ تا ۱۰۰ (<see cref="ImageQuality"/>). ۰ = نامعلوم ← ۵۰.</param>
    /// <param name="strict">سخت‌گیر (عکس دسته): عکس صحنه هم رد می‌شود. در حالت عادی صحنه فقط امتیاز کم می‌گیرد.</param>
    public static SubjectVerdict Check(ReadOnlySpan<byte> rgb, int width, int height, double match = 0.5, int quality = 0, bool strict = false)
    {
        var reasons = new List<string>();
        if (width < 8 || height < 8 || rgb.Length < width * height * 3)
            return new SubjectVerdict(false, false, 0, false, 0, false, false, false, 0, false, ["عکس خوانده نشد"]);

        // ── ۱) حاشیه ──
        var ring = Math.Max(2, (int)Math.Round(Math.Min(width, height) * 0.06));
        var border = new List<(byte R, byte G, byte B)>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= ring && x < width - ring && y >= ring && y < height - ring)
                    continue;
                var i = (y * width + x) * 3;
                border.Add((rgb[i], rgb[i + 1], rgb[i + 2]));
            }
        }
        var whiteBorder = border.Count(IsWhite) / (double)border.Count;
        var bg = Median(border);
        var plainBorder = border.Count(c => Distance(c, bg) <= 30) / (double)border.Count;
        var white = whiteBorder >= 0.80;
        var plain = white || plainBorder >= 0.75;

        // ── ۲) سوژه ──
        int minX = width, minY = height, maxX = -1, maxY = -1, fg = 0, skin = 0, ghost = 0, bgCount = 0;
        var fgMask = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                var c = (rgb[i], rgb[i + 1], rgb[i + 2]);
                var d = plain ? Distance(c, bg) : 999;
                if (d > 38)
                {
                    fg++;
                    fgMask[y * width + x] = true;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    if (IsSkin(c))
                        skin++;
                }
                else
                {
                    bgCount++;
                    if (d >= 10)
                        ghost++;
                }
            }
        }
        var total = width * height;
        var coverage = fg / (double)total;
        var skinShare = fg == 0 ? 0 : skin / (double)fg;
        var centered = fg > 0 && Math.Abs((minX + maxX) / 2.0 - width / 2.0) <= width * 0.25 && Math.Abs((minY + maxY) / 2.0 - height / 2.0) <= height * 0.25;

        // ── ۳) آدم و صحنه ──
        var lifestyle = !plain;
        var people = lifestyle ? skinShare >= 0.22 : skinShare >= 0.45;

        // ── ۴) واترمارک/لوگوی گوشه ──
        var watermark = false;
        if (plain)
        {
            var cw = Math.Max(2, width * 15 / 100);
            var ch = Math.Max(2, height * 15 / 100);
            var cornerHits = 0;
            foreach (var (cx, cy) in new[] { (0, 0), (width - cw, 0), (0, height - ch), (width - cw, height - ch) })
            {
                var count = 0;
                for (var y = cy; y < cy + ch; y++)
                    for (var x = cx; x < cx + cw; x++)
                        if (fgMask[y * width + x])
                            count++;
                var share = count / (double)(cw * ch);
                // لکهٔ کوچک (۰٫۵٪ تا ۲۰٪) در گوشه در حالی که سوژهٔ اصلی وسط است ← لوگو/مهر.
                if (share is >= 0.005 and <= 0.20 && !TouchesCenterBlob(fgMask, width, height, cx, cy, cw, ch))
                    cornerHits++;
            }
            var centerShare = CenterShare(fgMask, width, height);
            if (cornerHits is >= 1 and <= 2 && centerShare >= 0.15)
                watermark = true;
            // متن شفاف استوک: بخش بزرگی (≥ ۳۰٪) از زمینهٔ سفید «تقریباً سفید ولی نه کاملاً» است. آستانه بالاست تا سایهٔ نرم
            // زیر کالا (رایج در عکس کاتالوگی) اشتباهی واترمارک حساب نشود.
            if (white && bgCount > 0 && ghost / (double)bgCount >= 0.30)
                watermark = true;
        }

        // ── ۵) امتیاز ──
        if (white) reasons.Add("زمینهٔ سفید");
        else if (plain) reasons.Add("زمینهٔ یکدست");
        else reasons.Add("عکس صحنه (زمینهٔ شلوغ)");
        if (coverage < 0.04) reasons.Add("سوژه خیلی کوچک است");
        if (people) reasons.Add("احتمال حضور آدم");
        if (watermark) reasons.Add("احتمال لوگو/واترمارک");

        var m = Math.Clamp(match, 0, 1);
        var q = (quality <= 0 ? 50 : Math.Clamp(quality, 0, 100)) / 100.0;
        var score = 0.30 * m + 0.15 * q
                    + (white ? 0.25 : plain ? 0.15 : 0.03)
                    + (coverage is >= 0.06 and <= 0.92 ? 0.12 : coverage is > 0.92 ? 0.05 : 0)
                    + (centered ? 0.06 : 0)
                    + (skinShare < 0.15 ? 0.10 : skinShare < 0.3 ? 0.04 : 0);
        if (people) score -= 0.45;
        if (watermark) score -= 0.40;
        if (lifestyle) score -= strict ? 0.30 : 0.15;
        score = Math.Round(Math.Clamp(score, 0, 1), 3);

        var accept = !people && !watermark && coverage >= 0.03 && score >= MinConfidence && (!strict || plain);
        return new SubjectVerdict(white, plain, Math.Round(coverage, 3), centered, Math.Round(skinShare, 3), people, lifestyle, watermark, score, accept, reasons);
    }

    /// <summary>پیکسل سفید: هر سه کانال ≥ ۲۲۸ و اختلاف کانال‌ها ≤ ۱۸ (سفید مایل به رنگ هم پذیرفته نیست).</summary>
    public static bool IsWhite((byte R, byte G, byte B) c) =>
        Math.Min(c.R, Math.Min(c.G, c.B)) >= 228 && Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) <= 18;

    /// <summary>
    /// رنگ پوست (ترکیب دو قاعدهٔ رایج RGB و YCbCr تا چوب و کرم کمتر اشتباه شود).
    /// مثال: (۲۲۴، ۱۷۲، ۱۳۸) ← true؛ آبی/خاکستری ← false.
    /// </summary>
    public static bool IsSkin((byte R, byte G, byte B) c)
    {
        int r = c.R, g = c.G, b = c.B;
        if (!(r > 95 && g > 40 && b > 20 && r > g && r > b && r - g > 15 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 15))
            return false;
        var cb = 128 - 0.168736 * r - 0.331264 * g + 0.5 * b;
        var cr = 128 + 0.5 * r - 0.418688 * g - 0.081312 * b;
        return cb is >= 77 and <= 127 && cr is >= 137 and <= 173;
    }

    /// <summary>فاصلهٔ رنگ (بیشینهٔ اختلاف کانال‌ها؛ سریع و کافی برای جدا کردن زمینه).</summary>
    public static int Distance((byte R, byte G, byte B) a, (byte R, byte G, byte B) b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    private static (byte R, byte G, byte B) Median(List<(byte R, byte G, byte B)> colors)
    {
        static byte Mid(IEnumerable<byte> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            return sorted.Length == 0 ? (byte)255 : sorted[sorted.Length / 2];
        }
        return (Mid(colors.Select(c => c.R)), Mid(colors.Select(c => c.G)), Mid(colors.Select(c => c.B)));
    }

    // سهم پیکسل‌های سوژه در ۵۰٪ میانی عکس.
    private static double CenterShare(bool[] mask, int width, int height)
    {
        int x0 = width / 4, x1 = width * 3 / 4, y0 = height / 4, y1 = height * 3 / 4, count = 0, all = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                all++;
                if (mask[y * width + x])
                    count++;
            }
        return all == 0 ? 0 : count / (double)all;
    }

    // آیا لکهٔ گوشه به سوژهٔ وسط چسبیده است؟ (لبهٔ داخلی گوشه سوژه دارد ← بخشی از خود کالاست، نه لوگو.)
    private static bool TouchesCenterBlob(bool[] mask, int width, int height, int cx, int cy, int cw, int ch)
    {
        var innerX = cx == 0 ? cw : cx - 1;
        var innerY = cy == 0 ? ch : cy - 1;
        var hits = 0;
        for (var y = cy; y < cy + ch; y++)
            if (innerX >= 0 && innerX < width && mask[y * width + innerX])
                hits++;
        for (var x = cx; x < cx + cw; x++)
            if (innerY >= 0 && innerY < height && mask[innerY * width + x])
                hits++;
        return hits >= 2;
    }
}
