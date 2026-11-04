using Fratak_Shop.Core.Utilities;
using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Media;

/// <summary>
/// تبدیل هر تصویر (JPG، PNG، GIF، BMP، WebP) به WebP – دور سئوی خودکار (موارد ۱۲۸ تا ۱۳۷: «هیچ PNG/JPG در سایت نماند»).
/// <para>چرا WebP؟ حجم ۲۵ تا ۷۰ درصد کمتر با همان کیفیت ← بارگذاری سریع‌تر (LCP بهتر) که مستقیماً در رتبهٔ گوگل اثر دارد.</para>
/// <para>قواعد:
/// <list type="bullet">
/// <item>کیفیت ۸۲ (تعادل کیفیت/حجم؛ تفاوت با اصل با چشم دیده نمی‌شود).</item>
/// <item>ضلع بزرگ‌تر حداکثر ۲۵۶۰ پیکسل (بزرگ‌تر کوچک می‌شود؛ هرگز بزرگ نمی‌شود).</item>
/// <item>جهت عکس موبایل (EXIF Orientation) اعمال می‌شود تا عکس کج نماند؛ همهٔ متادیتا (GPS، دوربین) حذف می‌شود.</item>
/// <item>برای PNG (معمولاً لوگو/گرافیک با لبهٔ تیز) نسخهٔ بی‌اتلاف هم ساخته می‌شود و اگر حداکثر ۲۰٪ بزرگ‌تر بود همان انتخاب می‌شود.</item>
/// <item>GIF متحرک: فقط فریم اول (آپلودهای پنل برای عکس محصول/مقاله‌اند).</item>
/// </list></para>
/// <para>کجا استفاده می‌شود؟ <c>UploadStore.SaveAsync</c>، <c>ReviewImageStore</c>، کار شبانهٔ تبدیل تصویرهای قدیمی
/// (<see cref="LegacyImageConverter"/>) و آیکون PNG لحظه‌ای برای iOS/ایمیل.</para>
/// </summary>
public static class WebPEncoder
{
    public const int Quality = 82;
    public const int MaxSide = 2560;
    private const long MaxPixels = 40_000_000;

    /// <summary>
    /// امضای GIF یا BMP؟ (این دو را <see cref="ImageSanitizer"/> نمی‌پذیرد؛ مستقیم با Skia به WebP تبدیل می‌شوند.)
    /// مثال: بایت‌های «GIF89a…» ← true.
    /// </summary>
    public static bool IsGifOrBmp(ReadOnlySpan<byte> data) =>
        (data.Length > 10 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
        || (data.Length > 26 && data[0] == (byte)'B' && data[1] == (byte)'M');

    /// <summary>
    /// تبدیل به WebP. ناموفق (فایل خراب، ابعاد غیرمجاز، نبود کتابخانهٔ بومی) ← null؛ استثنا نمی‌دهد.
    /// <example><code>
    /// var webp = WebPEncoder.ToWebP(jpgBytes);   // webp.Extension == ".webp"، webp.ContentType == "image/webp"
    /// </code></example>
    /// </summary>
    public static SanitizedImage? ToWebP(byte[] data, ILogger? logger = null, bool tryLossless = false)
    {
        try
        {
            using var stream = new SKMemoryStream(data);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
                return null;
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
                return null;
            var lossless = tryLossless || codec.EncodedFormat == SKEncodedImageFormat.Png;

            using var decoded = SKBitmap.Decode(codec);
            if (decoded is null)
                return null;
            using var oriented = MediaEndpoints.Orient(decoded, codec.EncodedOrigin);
            var bitmap = oriented ?? decoded;

            SKBitmap? resized = null;
            try
            {
                var longest = Math.Max(bitmap.Width, bitmap.Height);
                if (longest > MaxSide)
                {
                    var scale = MaxSide / (double)longest;
                    var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                    var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
                    resized = bitmap.Resize(new SKImageInfo(width, height, bitmap.ColorType, bitmap.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell));
                }

                var final = resized ?? bitmap;
                var bytes = Encode(final, lossless);
                if (bytes is null)
                    return null;
                return new SanitizedImage(bytes, ".webp", "image/webp", final.Width, final.Height);
            }
            finally
            {
                resized?.Dispose();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // نبود کتابخانهٔ بومی (DllNotFoundException/TypeInitializationException) یا فایل خراب: فایل اصلی می‌ماند و کار شبانه دوباره تلاش می‌کند.
            logger?.LogWarning(exception, "WebP conversion failed");
            return null;
        }
    }

    /// <summary>کدگذاری WebP؛ برای گرافیک (PNG) بی‌اتلاف اگر حداکثر ۲۰٪ بزرگ‌تر از نسخهٔ کیفیت ۸۲ باشد.</summary>
    private static byte[]? Encode(SKBitmap bitmap, bool tryLossless)
    {
        using var pixmap = bitmap.PeekPixels();
        if (pixmap is null)
            return null;
        using var lossy = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, Quality));
        var lossyBytes = lossy?.ToArray();
        if (!tryLossless)
            return lossyBytes;
        using var exact = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 75));
        var exactBytes = exact?.ToArray();
        if (exactBytes is null)
            return lossyBytes;
        if (lossyBytes is null || exactBytes.Length <= lossyBytes.Length * 1.2)
            return exactBytes;
        return lossyBytes;
    }

    /// <summary>
    /// PNG مربعی برای جاهایی که WebP پشتیبانی نمی‌شود (آیکون خانهٔ iOS، لوگوی ایمیل). ناموفق ← null.
    /// مثال: <c>ToPng(webpBytes, 180)</c> ← PNG ۱۸۰×۱۸۰ (تصویر وسط و بدون کشیدگی).
    /// </summary>
    public static byte[]? ToPng(byte[] data, int size, bool square = true)
    {
        try
        {
            using var decoded = SKBitmap.Decode(data);
            if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
                return null;
            var scale = Math.Min(size / (double)decoded.Width, size / (double)decoded.Height);
            var width = Math.Max(1, (int)Math.Round(decoded.Width * scale));
            var height = Math.Max(1, (int)Math.Round(decoded.Height * scale));
            var canvasWidth = square ? size : width;
            var canvasHeight = square ? size : height;
            using var surface = new SKBitmap(new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(surface))
            {
                canvas.Clear(SKColors.Transparent);
                using var image = SKImage.FromBitmap(decoded);
                var left = (canvasWidth - width) / 2f;
                var top = (canvasHeight - height) / 2f;
                canvas.DrawImage(image, new SKRect(left, top, left + width, top + height), new SKSamplingOptions(SKCubicResampler.Mitchell));
            }

            using var png = SKImage.FromBitmap(surface).Encode(SKEncodedImageFormat.Png, 100);
            return png?.ToArray();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// گام مشترک آپلودها: تصویر پاک‌سازی‌شده ← WebP. اگر تبدیل نشد همان تصویر پاک‌شده می‌ماند (و لاگ می‌شود) تا آپلود
    /// کاربر خراب نشود؛ کار شبانه بعداً دوباره تلاش می‌کند.
    /// </summary>
    public static SanitizedImage Normalize(SanitizedImage image, ILogger? logger = null)
    {
        if (image.Extension == ".webp" && Math.Max(image.Width, image.Height) <= MaxSide)
            return image;
        var webp = ToWebP(image.Data, logger);
        if (webp is null)
        {
            logger?.LogWarning("Upload kept as {Extension}: WebP conversion was not possible", image.Extension);
            return image;
        }

        // خروجی هم از پاک‌ساز رد می‌شود (اطمینان از سالم بودن و نبود متادیتا).
        return ImageSanitizer.Sanitize(webp.Data, out _) ?? image;
    }

    /// <summary>بایت‌های خام آپلود ← تصویر نهایی WebP، یا null با پیام خطا (GIF/BMP هم پذیرفته می‌شوند).</summary>
    public static SanitizedImage? FromUpload(byte[] bytes, out string? error, ILogger? logger = null)
    {
        var image = ImageSanitizer.Sanitize(bytes, out error);
        if (image is not null)
            return Normalize(image, logger);
        if (!IsGifOrBmp(bytes))
            return null;
        var webp = ToWebP(bytes, logger);
        var clean = webp is null ? null : ImageSanitizer.Sanitize(webp.Data, out _);
        if (clean is null)
            return null;
        error = null;
        return clean;
    }
}
