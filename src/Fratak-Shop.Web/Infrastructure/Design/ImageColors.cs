using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Design;

/// <summary>
/// تحلیل رنگ عکس کالا برای طراحی بنر (دور ۲۳)، بدون هوش مصنوعی:
/// <list type="bullet">
/// <item><see cref="Dominant"/>: رنگ غالب «رنگی» عکس (سفید/سیاه/خاکستری پس‌زمینه نادیده) ← کلید پالت «#rrggbb».</item>
/// <item><see cref="HasWhiteBackground"/> و <see cref="CutoutWhite"/>: عکس کالا روی زمینهٔ سفید (رایج در فروشگاه‌ها) ← پس‌زمینهٔ سفید
/// با «پر کردن از لبه‌ها» شفاف می‌شود تا کالا روی رنگ بنر بنشیند (سفیدیِ داخل خود کالا دست نمی‌خورد چون به لبه وصل نیست).</item>
/// </list>
/// <para>کار روی نسخهٔ کوچک‌شده (حداکثر ۹۶ پیکسل برای رنگ، ۸۰۰ برای برش) تا روی هاست ضعیف هم سریع باشد.</para>
/// <para>محل استفاده: DesignStudio.BuildFromBriefAsync.</para>
/// </summary>
public static class ImageColors
{
    /// <summary>
    /// رنگ غالب رنگی عکس یا null (عکس بی‌رنگ/سیاه‌وسفید یا خراب). مثال: عکس هدفون قرمز روی زمینهٔ سفید ← «#c81e2a» (تقریبی).
    /// </summary>
    public static string? Dominant(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var small = Resize(source, 96);
        if (small is null)
            return null;
        // ۳۶ سطل رنگ (هر ۱۰ درجه)؛ وزن = اشباع × تعداد.
        var weights = new double[36];
        var sums = new (double R, double G, double B, double W)[36];
        for (var y = 0; y < small.Height; y++)
        {
            for (var x = 0; x < small.Width; x++)
            {
                var c = small.GetPixel(x, y);
                if (c.Alpha < 128)
                    continue;
                c.ToHsv(out var h, out var s, out var v);
                if (s < 25 || v < 20 || v > 98)
                    continue; // خاکستری، سیاه یا سفید پس‌زمینه
                var bucket = Math.Clamp((int)(h / 10), 0, 35);
                var w = s / 100.0;
                weights[bucket] += w;
                var t = sums[bucket];
                sums[bucket] = (t.R + c.Red * w, t.G + c.Green * w, t.B + c.Blue * w, t.W + w);
            }
        }

        var best = Array.IndexOf(weights, weights.Max());
        var total = small.Width * small.Height;
        if (best < 0 || sums[best].W <= 0 || weights[best] < total * 0.02)
            return null;
        var s0 = sums[best];
        return Fratak_Shop.Core.Services.Design.DesignColors.ToHex(((int)(s0.R / s0.W), (int)(s0.G / s0.W), (int)(s0.B / s0.W)));
    }

    /// <summary>
    /// زمینهٔ سفید؟ دست‌کم ۸۵٪ پیکسل‌های قاب بیرونی (۲٪ لبه‌ها) تقریباً سفید باشند. مثال: عکس استودیویی کالا ← true؛ عکس محیطی ← false.
    /// </summary>
    public static bool HasWhiteBackground(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var small = Resize(source, 200);
        if (small is null)
            return false;
        var border = Math.Max(1, small.Width / 50);
        int white = 0, all = 0;
        for (var y = 0; y < small.Height; y++)
        {
            for (var x = 0; x < small.Width; x++)
            {
                if (x >= border && x < small.Width - border && y >= border && y < small.Height - border)
                    continue;
                all++;
                if (IsWhite(small.GetPixel(x, y)))
                    white++;
            }
        }
        return all > 0 && white >= all * 0.85;
    }

    /// <summary>
    /// برش زمینهٔ سفید (سازگاری با دور ۲۳): همان <see cref="Cutout"/> با سقف ۸۰۰ پیکسل؛ خروجی فقط PNG یا null.
    /// </summary>
    public static byte[]? CutoutWhite(SKBitmap source) => Cutout(source, 800)?.Png;

    /// <summary>
    /// دور ۲۷ – «دوربری» حرفه‌ای عکس کالا با زمینهٔ سفید (بدون هوش مصنوعی و بدون سرویس بیرونی):
    /// <list type="number">
    /// <item>پیکسل‌های تقریباً سفید/خاکستری خیلی روشنِ متصل به لبه‌ها (و پیکسل‌های از قبل شفاف) با «پر کردن از لبه» زمینه شناخته می‌شوند؛
    /// سفیدیِ داخل خود کالا (مثل صفحهٔ سفید گوشی) چون به لبه وصل نیست دست نمی‌خورد.</item>
    /// <item>لبهٔ نرم: پیکسل‌های مرزی بر اساس «چقدر سفیدند» نیمه‌شفاف می‌شوند و رنگشان از ترکیب با سفید پاک می‌شود (هالهٔ سفید دور کالا نمی‌ماند).</item>
    /// <item>حاشیهٔ خالی بریده می‌شود (کالا در بنر بزرگ و هم‌اندازه دیده شود) + ۲٪ فاصلهٔ امن.</item>
    /// </list>
    /// خروجی null: تصویر خالی، چیزی برای برش نبود (زمینه سفید نیست) یا تقریباً همه‌اش زمینه بود (کالا گم می‌شد).
    /// مثال: عکس ۱۲۰۰×۱۲۰۰ هدفون مشکی روی سفید ← PNG حدود ۷۲۰×۸۷۰ با زمینهٔ شفاف.
    /// </summary>
    public static CutoutResult? Cutout(SKBitmap source, int maxSide = 900)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var bitmap = Resize(source, Math.Clamp(maxSide, 64, 2000));
        if (bitmap is null)
            return null;
        int w = bitmap.Width, h = bitmap.Height;
        // خواندن پیکسل‌ها با GetPixel (مستقل از قالب رنگ تصویر: RGB565، خاکستری، BGRA و …)
        var pixels = new SKColor[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                pixels[y * w + x] = bitmap.GetPixel(x, y);
        var background = new bool[w * h];
        var queue = new Queue<int>();
        void Seed(int i)
        {
            if (!background[i] && IsBackground(pixels[i]))
            {
                background[i] = true;
                queue.Enqueue(i);
            }
        }

        for (var x = 0; x < w; x++)
        {
            Seed(x);
            Seed((h - 1) * w + x);
        }
        for (var y = 0; y < h; y++)
        {
            Seed(y * w);
            Seed(y * w + w - 1);
        }
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            int x = i % w, y = i / w;
            if (x > 0) Seed(i - 1);
            if (x < w - 1) Seed(i + 1);
            if (y > 0) Seed(i - w);
            if (y < h - 1) Seed(i + w);
        }

        // کادر کالا (پیکسل‌های غیرزمینه) و سهم آن از کل تصویر
        int minX = w, minY = h, maxX = -1, maxY = -1, kept = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (background[y * w + x])
                    continue;
                kept++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        var coverage = kept / (double)(w * h);
        if (maxX < 0 || coverage < 0.03 || coverage > 0.985)
            return null;
        var pad = Math.Max(2, (int)Math.Round(Math.Max(w, h) * 0.02));
        minX = Math.Max(0, minX - pad);
        minY = Math.Max(0, minY - pad);
        maxX = Math.Min(w - 1, maxX + pad);
        maxY = Math.Min(h - 1, maxY + pad);
        int cw = maxX - minX + 1, ch = maxY - minY + 1;

        var output = new SKColor[cw * ch];
        for (var y = 0; y < ch; y++)
        {
            for (var x = 0; x < cw; x++)
            {
                int sx = x + minX, sy = y + minY, i = sy * w + sx;
                if (background[i])
                {
                    output[y * cw + x] = SKColors.Transparent;
                    continue;
                }
                var c = pixels[i];
                var edge = (sx > 0 && background[i - 1]) || (sx < w - 1 && background[i + 1]) || (sy > 0 && background[i - w]) || (sy < h - 1 && background[i + w]);
                if (!edge)
                {
                    output[y * cw + x] = c;
                    continue;
                }
                // لبه: هرچه به سفید نزدیک‌تر، شفاف‌تر؛ رنگ هم «از سفید جدا» می‌شود: c = a·رنگ‌واقعی + (1−a)·سفید.
                var min = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
                var a = Math.Clamp((255 - min) / 55.0, 0.2, 1.0) * (c.Alpha / 255.0);
                byte Unmix(byte v) => (byte)Math.Clamp(Math.Round((v - 255 * (1 - a)) / Math.Max(a, 0.2)), 0, 255);
                output[y * cw + x] = new SKColor(Unmix(c.Red), Unmix(c.Green), Unmix(c.Blue), (byte)Math.Round(a * 255));
            }
        }

        using var result = new SKBitmap(new SKImageInfo(cw, ch, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < ch; y++)
            for (var x = 0; x < cw; x++)
                result.SetPixel(x, y, output[y * cw + x]);
        using var image = SKImage.FromBitmap(result);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data is null ? null : new CutoutResult(data.ToArray(), cw, ch, coverage);
    }

    /// <summary>
    /// زمینهٔ قابل برش: از قبل شفاف، یا تقریباً سفید/خاکستری خیلی روشنِ بی‌رنگ (هر سه کانال ≥ ۲۳۲ و اختلاف کانال‌ها ≤ ۱۸).
    /// مثال: #f7f7f7 ← بله؛ #ffe4e1 (صورتی روشن) ← نه (رنگ کالاست، نه زمینه).
    /// </summary>
    public static bool IsBackground(SKColor c)
    {
        if (c.Alpha < 24)
            return true;
        if (c.Alpha <= 200)
            return false;
        var min = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
        var max = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
        return min >= 232 && max - min <= 18;
    }

    /// <summary>
    /// دور ۲۷ – عکسِ از قبل «دوربری‌شده»؟ (کانال شفافیت دارد و دست‌کم ۳۰٪ پیکسل‌های قاب بیرونی شفاف‌اند).
    /// مثال: PNG کالای بدون زمینه ← true؛ WebP معمولی ← false.
    /// </summary>
    public static bool HasTransparentBorder(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.AlphaType == SKAlphaType.Opaque)
            return false;
        using var small = Resize(source, 200);
        if (small is null)
            return false;
        var border = Math.Max(1, small.Width / 50);
        int clear = 0, all = 0;
        for (var y = 0; y < small.Height; y++)
        {
            for (var x = 0; x < small.Width; x++)
            {
                if (x >= border && x < small.Width - border && y >= border && y < small.Height - border)
                    continue;
                all++;
                if (small.GetPixel(x, y).Alpha < 32)
                    clear++;
            }
        }
        return all > 0 && clear >= all * 0.3;
    }

    /// <summary>تقریباً سفید (هر سه کانال ≥ ۲۳۵ و کدر).</summary>
    public static bool IsWhite(SKColor c) => c.Alpha > 200 && c.Red >= 235 && c.Green >= 235 && c.Blue >= 235;

    private static SKBitmap? Resize(SKBitmap source, int maxSide)
    {
        if (source.Width <= 0 || source.Height <= 0)
            return null;
        var scale = Math.Min(1.0, maxSide / (double)Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var info = new SKImageInfo(width, height, source.ColorType, source.AlphaType);
        return source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear));
    }
}

/// <summary>دور ۲۷ – نتیجهٔ دوربری: PNG شفاف، ابعاد (بعد از بریدن حاشیه) و سهم کالا از تصویر اصلی (۰ تا ۱).</summary>
public sealed record CutoutResult(byte[] Png, int Width, int Height, double Coverage);
