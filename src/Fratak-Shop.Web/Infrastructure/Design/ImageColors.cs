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
    /// برش زمینهٔ سفید: پیکسل‌های تقریباً سفیدِ متصل به لبه‌ها شفاف می‌شوند (لبهٔ نرم یک پیکسلی). خروجی PNG یا null.
    /// عکس بزرگ‌تر از ۸۰۰ پیکسل اول کوچک می‌شود (برای بنر کافی است).
    /// </summary>
    public static byte[]? CutoutWhite(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var bitmap = Resize(source, 800);
        if (bitmap is null)
            return null;
        int w = bitmap.Width, h = bitmap.Height;
        var transparent = new bool[w * h];
        var queue = new Queue<int>();
        void Seed(int x, int y)
        {
            var i = y * w + x;
            if (!transparent[i] && IsWhite(bitmap.GetPixel(x, y)))
            {
                transparent[i] = true;
                queue.Enqueue(i);
            }
        }

        for (var x = 0; x < w; x++)
        {
            Seed(x, 0);
            Seed(x, h - 1);
        }
        for (var y = 0; y < h; y++)
        {
            Seed(0, y);
            Seed(w - 1, y);
        }
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            int x = i % w, y = i / w;
            if (x > 0) Seed(x - 1, y);
            if (x < w - 1) Seed(x + 1, y);
            if (y > 0) Seed(x, y - 1);
            if (y < h - 1) Seed(x, y + 1);
        }

        using var output = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = bitmap.GetPixel(x, y);
                var i = y * w + x;
                byte alpha = c.Alpha;
                if (transparent[i])
                    alpha = 0;
                else if ((x > 0 && transparent[i - 1]) || (x < w - 1 && transparent[i + 1]) || (y > 0 && transparent[i - w]) || (y < h - 1 && transparent[i + w]))
                    alpha = (byte)(alpha * 0.6); // لبهٔ نرم
                output.SetPixel(x, y, new SKColor(c.Red, c.Green, c.Blue, alpha));
            }
        }

        using var image = SKImage.FromBitmap(output);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
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
