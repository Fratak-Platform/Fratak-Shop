namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// دور ۳۱ (#۸) – «قاب دور عکس»: بعضی تأمین‌کننده‌ها دور عکس کالا یک قاب رنگی (نوار یکدست سرمه‌ای/طلایی/خاکستری) می‌کشند.
/// مدل دوربری گاهی همین قاب را هم «سوژه» می‌بیند و نتیجه رد می‌شود. این کلاس قاب را پیدا می‌کند تا پیش از دوربری بریده شود.
/// <para>قاعده (خالص و آزمون‌پذیر، روی نسخهٔ کوچک RGB):</para>
/// <list type="number">
/// <item>از هر لبه به داخل، خط‌به‌خط جلو می‌رویم تا وقتی خط «یکدست» است (۹۷٪ پیکسل‌ها نزدیک میانگین همان خط)؛ حداکثر ۱۸٪ ضلع.</item>
/// <item>آن لبه «قاب» دارد اگر نوار حداقل ۲ خط باشد، قبل از سقف تمام شود و رنگش با رنگ زمینهٔ داخل نوار (پیکسل‌های دو سر خط بعدی)
/// فاصلهٔ رنگی بیش از <see cref="MinContrast"/> داشته باشد. زمینهٔ سفید ساده قاب حساب نمی‌شود (رنگ نوار = رنگ داخل).</item>
/// <item>قاب فقط وقتی پذیرفته می‌شود که حداقل ۳ لبه (یا دو لبهٔ روبه‌رو) قاب داشته باشند؛ نوار تکی (مثلاً سایهٔ پایین) قاب نیست.</item>
/// </list>
/// مثال: عکس ۱۰۰×۱۰۰ با قاب سرمه‌ای ۶ پیکسلی دور زمینهٔ سفید ← کادر داخلی (۷، ۷، ۹۳، ۹۳)؛ عکس با زمینهٔ سفید ساده ← null.
/// </summary>
public static class ImageFrame
{
    /// <summary>کمترین فاصلهٔ رنگی نوار قاب با زمینهٔ داخل آن.</summary>
    public const double MinContrast = 45;

    /// <summary>بیشترین پهنای قاب نسبت به ضلع عکس.</summary>
    public const double MaxBand = 0.18;

    /// <summary>کادر داخلی بعد از برداشتن قاب (مختصات همان نسخهٔ کوچک؛ Right/Bottom انحصاری).</summary>
    public readonly record struct Box(int Left, int Top, int Right, int Bottom)
    {
        /// <summary>پهنای کادر.</summary>
        public int Width => Right - Left;

        /// <summary>ارتفاع کادر.</summary>
        public int Height => Bottom - Top;
    }

    /// <summary>
    /// کادر داخل قاب یا null (قاب ندارد/ورودی نامعتبر). <paramref name="rgb"/> = بایت‌های RGB پشت سر هم (w×h×3).
    /// </summary>
    public static Box? Inner(byte[] rgb, int width, int height)
    {
        if (rgb is null || width < 24 || height < 24 || rgb.Length < width * height * 3)
            return null;

        var top = Band(rgb, width, height, Side.Top);
        var bottom = Band(rgb, width, height, Side.Bottom);
        var left = Band(rgb, width, height, Side.Left);
        var right = Band(rgb, width, height, Side.Right);

        var framed = (top > 0 ? 1 : 0) + (bottom > 0 ? 1 : 0) + (left > 0 ? 1 : 0) + (right > 0 ? 1 : 0);
        var opposite = (top > 0 && bottom > 0) || (left > 0 && right > 0);
        if (framed < 3 && !(framed == 2 && opposite))
            return null;

        // یک خط اضافه برای لبهٔ نرم (ضدپله) قاب.
        var box = new Box(left > 0 ? left + 1 : 0, top > 0 ? top + 1 : 0, right > 0 ? width - right - 1 : width, bottom > 0 ? height - bottom - 1 : height);
        // کادر داخلی باید هنوز بخش اصلی عکس باشد (حداقل ۵۰٪ هر ضلع).
        return box.Width >= width / 2 && box.Height >= height / 2 ? box : null;
    }

    private enum Side
    {
        Top,
        Bottom,
        Left,
        Right,
    }

    /// <summary>پهنای نوار قاب روی یک لبه (۰ = قاب ندارد).</summary>
    private static int Band(byte[] rgb, int width, int height, Side side)
    {
        var vertical = side is Side.Left or Side.Right;
        var length = vertical ? height : width;     // طول هر خط
        var depthMax = (int)((vertical ? width : height) * MaxBand);
        if (depthMax < 3)
            return 0;

        (double R, double G, double B)? bandColor = null;
        var depth = 0;
        while (depth < depthMax)
        {
            var (uniform, mean) = Line(rgb, width, height, side, depth, length);
            if (!uniform)
                break;
            if (bandColor is { } color && Distance(color, mean) > 24)
                break; // رنگ عوض شد: نوار قبلی تمام شد (قاب دولایه/زمینهٔ یکدست داخل قاب)
            bandColor ??= mean;
            depth++;
        }
        if (bandColor is null || depth < 2 || depth >= depthMax)
            return 0;

        // رنگ زمینهٔ داخل نوار: دو سر خطِ بعدی (کالا معمولاً وسط است، دو سر خط زمینه است).
        var inner = Ends(rgb, width, height, side, depth, length);
        return Distance(bandColor.Value, inner) > MinContrast ? depth : 0;
    }

    /// <summary>یکدست بودن یک خط و میانگین رنگش.</summary>
    private static (bool Uniform, (double R, double G, double B) Mean) Line(byte[] rgb, int width, int height, Side side, int depth, int length)
    {
        double r = 0, g = 0, b = 0;
        for (var k = 0; k < length; k++)
        {
            var i = Index(width, height, side, depth, k);
            r += rgb[i];
            g += rgb[i + 1];
            b += rgb[i + 2];
        }
        var mean = (r / length, g / length, b / length);
        var near = 0;
        for (var k = 0; k < length; k++)
        {
            var i = Index(width, height, side, depth, k);
            if (Distance(mean, (rgb[i], rgb[i + 1], rgb[i + 2])) <= 30)
                near++;
        }
        return (near >= length * 0.97, mean);
    }

    /// <summary>میانگین رنگ ۱۵٪ ابتدا و انتهای یک خط.</summary>
    private static (double R, double G, double B) Ends(byte[] rgb, int width, int height, Side side, int depth, int length)
    {
        var span = Math.Max(2, length * 15 / 100);
        double r = 0, g = 0, b = 0;
        var n = 0;
        for (var k = 0; k < length; k++)
        {
            if (k >= span && k < length - span)
                continue;
            var i = Index(width, height, side, depth, k);
            r += rgb[i];
            g += rgb[i + 1];
            b += rgb[i + 2];
            n++;
        }
        return n == 0 ? (0, 0, 0) : (r / n, g / n, b / n);
    }

    /// <summary>جای پیکسل k-ام خطِ «عمق depth» از یک لبه در آرایهٔ RGB.</summary>
    private static int Index(int width, int height, Side side, int depth, int k)
    {
        var (x, y) = side switch
        {
            Side.Top => (k, depth),
            Side.Bottom => (k, height - 1 - depth),
            Side.Left => (depth, k),
            _ => (width - 1 - depth, k),
        };
        return (y * width + x) * 3;
    }

    private static double Distance((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }
}
