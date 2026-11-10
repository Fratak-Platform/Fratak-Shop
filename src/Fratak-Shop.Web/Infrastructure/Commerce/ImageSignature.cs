using System.Numerics;
using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Commerce;

/// <summary>
/// دور ۲۴ (پیشنهاد ۲۹ – جست‌وجو با عکس): «اثر انگشت» سبک یک تصویر، کاملاً روی خود سرور و بدون هیچ سرویس بیرونی.
/// <para>
/// دو بخش دارد:
/// (۱) هش تفاضلی (dHash) ۶۴ بیتی: تصویر به ۹×۸ خاکستری کوچک می‌شود و برای هر ردیف «روشن‌تر بودن هر نقطه از نقطهٔ کناری» یک بیت است.
/// دو تصویر هم‌شکل فاصلهٔ همینگ کمی دارند (مثلاً کمتر از ۱۲ از ۶۴).
/// (۲) هیستوگرام رنگ ۳۲ خانه‌ای: ۱۲ فام × ۲ سطح اشباع + ۸ سطح خاکستری؛ پیکسل‌های تقریباً سفید (پس‌زمینهٔ عکس کالا) و شفاف حساب نمی‌شوند.
/// </para>
/// <para>مثال: عکس یک کتانی قرمز ← Hash=…، Colors پر در خانه‌های قرمز؛ امتیاز شباهت با عکس کاتالوگ همان کتانی معمولاً بالای ۰٫۷ است.</para>
/// <para>محل استفاده: <see cref="ImageSearchService"/> (ساخت امضای کالاها و مقایسه با عکس مشتری) و آزمون‌های واحد.</para>
/// </summary>
public static class ImageSignature
{
    /// <summary>بیشترین حجم عکس ورودی مشتری (۴ مگابایت).</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>بیشترین تعداد پیکسل عکس (جلوگیری از «بمب تصویری»).</summary>
    public const long MaxPixels = 40_000_000;

    /// <summary>تعداد خانه‌های هیستوگرام رنگ.</summary>
    public const int Bins = 32;

    /// <summary>امضای یک تصویر.</summary>
    public sealed record Signature(long Hash, byte[] Colors);

    /// <summary>
    /// امضای بایت‌های یک تصویر (JPEG/PNG/WebP/GIF). تصویر خراب، خیلی بزرگ یا غیرتصویری ← null (هرگز استثنا نمی‌دهد).
    /// عکس‌های بزرگ از همان ابتدا کوچک‌شده باز می‌شوند تا حافظه کم مصرف شود.
    /// </summary>
    public static Signature? Compute(byte[] data)
    {
        if (data is null || data.Length == 0 || data.Length > MaxBytes * 4)
            return null;
        try
        {
            using var stream = new SKMemoryStream(data);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
                return null;
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
                return null;
            using var bitmap = DecodeSmall(codec);
            return bitmap is null ? null : Compute(bitmap);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>امضای یک تصویر بازشده.</summary>
    public static Signature? Compute(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return null;
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        using var tiny = bitmap.Resize(new SKImageInfo(9, 8, SKColorType.Rgba8888, SKAlphaType.Unpremul), sampling);
        using var small = bitmap.Resize(new SKImageInfo(32, 32, SKColorType.Rgba8888, SKAlphaType.Unpremul), sampling);
        if (tiny is null || small is null)
            return null;

        long hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (Gray(tiny.GetPixel(x, y)) > Gray(tiny.GetPixel(x + 1, y)))
                    hash |= 1L << bit;
                bit++;
            }
        }

        var counts = new int[Bins];
        var total = 0;
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                var bin = Bin(small.GetPixel(x, y));
                if (bin < 0)
                    continue;
                counts[bin]++;
                total++;
            }
        }
        return new Signature(hash, Normalize(counts, total));
    }

    /// <summary>
    /// خانهٔ هیستوگرام یک رنگ؛ ‎-1 یعنی حساب نشود (شفاف یا تقریباً سفید).
    /// مثال: قرمز خالص ← ۱ (فام ۰، اشباع زیاد)؛ خاکستری میانه ← خانه‌ای بین ۲۴ تا ۳۱؛ سفید ← ‎-1.
    /// </summary>
    public static int Bin(SKColor color)
    {
        if (color.Alpha < 128)
            return -1;
        color.ToHsv(out var h, out var s, out var v);
        if (v > 92 && s < 8)
            return -1;
        if (v < 18 || s < 15)
            return 24 + Math.Clamp((int)(v / 12.5f), 0, 7);
        var hue = Math.Clamp((int)(h / 30f), 0, 11);
        return hue * 2 + (s < 50 ? 0 : 1);
    }

    /// <summary>
    /// شمارش‌ها ← بایت‌ها (مجموع حدود ۱۰۲۰، هر خانه حداکثر ۲۵۵). مثال: همه در یک خانه ← آن خانه ۲۵۵، بقیه ۰. total=0 ← همه صفر.
    /// </summary>
    public static byte[] Normalize(int[] counts, int total)
    {
        var result = new byte[Bins];
        if (total <= 0)
            return result;
        for (var i = 0; i < Bins && i < counts.Length; i++)
            result[i] = (byte)Math.Clamp((int)Math.Round(counts[i] * 1020.0 / total), 0, 255);
        return result;
    }

    /// <summary>فاصلهٔ همینگ دو هش (۰ تا ۶۴). مثال: (0، 0) ← 0؛ (0، ‎-1) ← 64.</summary>
    public static int Hamming(long a, long b) => BitOperations.PopCount((ulong)(a ^ b));

    /// <summary>
    /// شباهت رنگ با «اشتراک هیستوگرام» (۰ تا ۱). مثال: دو آرایهٔ یکسان غیرصفر ← 1؛ بدون خانهٔ مشترک ← 0؛ آرایهٔ تمام‌صفر ← 0.
    /// </summary>
    public static double ColorSimilarity(byte[] a, byte[] b)
    {
        if (a is null || b is null || a.Length != b.Length)
            return 0;
        int shared = 0, sumA = 0, sumB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            shared += Math.Min(a[i], b[i]);
            sumA += a[i];
            sumB += b[i];
        }
        var denominator = Math.Max(sumA, sumB);
        return denominator == 0 ? 0 : (double)shared / denominator;
    }

    /// <summary>
    /// امتیاز نهایی شباهت (۰ تا ۱): ۵۵٪ شکل + ۴۵٪ رنگ. مثال: همان تصویر ← 1.
    /// </summary>
    public static double Score(Signature query, long hash, byte[] colors) =>
        0.55 * (1 - Hamming(query.Hash, hash) / 64.0) + 0.45 * ColorSimilarity(query.Colors, colors);

    private static float Gray(SKColor c) =>
        c.Alpha < 128 ? 255f : 0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue;

    /// <summary>باز کردن کوچک‌شده (تا حدود ۲۵۶ نقطه در ضلع بزرگ؛ JPEG مستقیم با ۱/۲ تا ۱/۸ باز می‌شود).</summary>
    private static SKBitmap? DecodeSmall(SKCodec codec)
    {
        var info = codec.Info;
        var longest = Math.Max(info.Width, info.Height);
        if (longest > 512)
        {
            var dims = codec.GetScaledDimensions(256f / longest);
            if (dims.Width > 0 && dims.Height > 0 && dims.Width < info.Width)
            {
                var scaled = new SKImageInfo(dims.Width, dims.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                var bitmap = new SKBitmap(scaled);
                var result = codec.GetPixels(scaled, bitmap.GetPixels());
                if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
                    return bitmap;
                bitmap.Dispose();
            }
        }
        var full = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var decoded = new SKBitmap(full);
        var status = codec.GetPixels(full, decoded.GetPixels());
        if (status is SKCodecResult.Success or SKCodecResult.IncompleteInput)
            return decoded;
        decoded.Dispose();
        return null;
    }
}
