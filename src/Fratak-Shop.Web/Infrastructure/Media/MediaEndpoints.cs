using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Fratak_Shop.Core.Services.Settings;
using Microsoft.Net.Http.Headers;
using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Media;

/// <summary>
/// Automatic image optimization: <c>/media/{width}/{path}</c> serves a resized copy of a local image
/// (WebP for browsers that accept it, otherwise the original format) for <c>srcset</c>.
/// Only fixed widths and only images under <c>uploads/</c> and <c>assets/image/</c> are allowed;
/// results are cached on disk (App_Data/media-cache), keyed by path, size, quality and the source's
/// modification time, so replacing an image produces a new copy. Oversized sources are refused.
/// </summary>
public static class MediaEndpoints
{
    public static readonly int[] Widths = [160, 320, 480, 640, 960, 1280];
    private static readonly string[] AllowedPrefixes = ["uploads/", "assets/image/"];
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private const long MaxSourceBytes = 20 * 1024 * 1024;
    private const long MaxSourcePixels = 40_000_000;

    /// <summary>
    /// مرحلهٔ هفدهم: سقف پیکسل در پروسهٔ ۳۲ بیتی (استخر IIS با «Enable 32-Bit Applications») کمتر است، چون کل فضای حافظهٔ
    /// پروسه حدود ۲ تا ۴ گیگابایت است. مثال: عکس ۲۴ مگاپیکسلی در پروسهٔ ۳۲ بیتی ← بدون تغییر اندازه، همان اصل فرستاده می‌شود.
    /// </summary>
    public static long PixelLimit => Environment.Is64BitProcess ? MaxSourcePixels : 16_000_000;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    /// <summary>
    /// مرحلهٔ هفدهم: حداکثر ۲ تغییر اندازهٔ هم‌زمان (قبلاً نصف هسته‌های سرور؛ روی سرور اشتراکی ۱۶ تا ۳۲ هسته‌ای یعنی ۸ تا ۱۶
    /// عکس بزرگ هم‌زمان در حافظه). باز کردن صفحه‌ای با ده‌ها عکسِ تازه دیگر حافظهٔ استخر را پر نمی‌کند؛ کمبود حافظه در
    /// کتابخانهٔ بومی SkiaSharp پروسه را بی‌خبر می‌بندد و چند بار تکرارش IIS را به خاموش کردن استخر (HTTP Error 503) می‌کشاند.
    /// </summary>
    private static readonly SemaphoreSlim Workers = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 2));

    /// <summary>
    /// حافظهٔ دستگاه/استخر تحت فشار است؟ (GC سقف حافظهٔ استخر IIS و Job Object را هم می‌بیند.) در این حالت عکس بدون تغییر
    /// اندازه فرستاده می‌شود تا تغییر اندازه باعث کمبود حافظه نشود.
    /// </summary>
    public static bool MemoryIsTight()
    {
        var info = GC.GetGCMemoryInfo();
        return info.HighMemoryLoadThresholdBytes > 0 && info.MemoryLoadBytes >= info.HighMemoryLoadThresholdBytes;
    }

    public static bool IsOptimizable(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.StartsWith('/') || url.StartsWith("//"))
            return false;

        var path = url.AsSpan(1);
        var query = path.IndexOfAny('?', '#');
        if (query >= 0)
            path = path[..query];

        var value = path.ToString();
        return AllowedPrefixes.Any(p => value.StartsWith(p, StringComparison.OrdinalIgnoreCase))
               && AllowedExtensions.Contains(Path.GetExtension(value).ToLowerInvariant())
               && !value.Contains("..");
    }

    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/media/{width:int}/{**path}", [HttpMethods.Get, HttpMethods.Head], async (int width, string path, HttpContext http,
            IWebHostEnvironment environment, IStoreSettingService settingService, ILoggerFactory loggerFactory) =>
        {
            if (Array.IndexOf(Widths, width) < 0 || !IsOptimizable("/" + path))
                return Results.NotFound();

            var webRoot = Path.GetFullPath(environment.WebRootPath);
            var source = Path.GetFullPath(Path.Combine(webRoot, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!source.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(source))
                return Results.NotFound();

            var info = new FileInfo(source);
            var settings = await settingService.GetAsync(http.RequestAborted);
            http.Response.Headers.CacheControl = "public, max-age=604800";
            http.Response.Headers.Vary = "Accept";

            var originalType = ContentType(info.Extension);
            if (!settings.IsImageOptimizationEnabled || info.Length > MaxSourceBytes || MemoryIsTight())
                return Results.File(source, originalType, lastModified: info.LastWriteTimeUtc, enableRangeProcessing: false);

            var acceptsWebp = http.Request.Headers.Accept.ToString().Contains("image/webp", StringComparison.OrdinalIgnoreCase);
            var format = acceptsWebp ? SKEncodedImageFormat.Webp
                : info.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? SKEncodedImageFormat.Png
                : SKEncodedImageFormat.Jpeg;
            var quality = Math.Clamp(settings.ImageQuality, 40, 95);

            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{path.ToLowerInvariant()}|{width}|{quality}|{format}|{info.LastWriteTimeUtc.Ticks}|{info.Length}")))[..32].ToLowerInvariant();
            var cacheDir = Path.Combine(environment.ContentRootPath, "App_Data", "media-cache", key[..2]);
            var extension = format switch { SKEncodedImageFormat.Webp => ".webp", SKEncodedImageFormat.Png => ".png", _ => ".jpg" };
            var cached = Path.Combine(cacheDir, key + extension);
            var contentType = ContentType(extension);

            if (!File.Exists(cached))
            {
                var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(http.RequestAborted);
                try
                {
                    if (!File.Exists(cached))
                    {
                        // صف شلوغ است (صفحهٔ پر از عکس تازه)؟ بعد از ۱۰ ثانیه همان اصل عکس فرستاده می‌شود تا درخواست معطل نماند.
                        if (!await Workers.WaitAsync(TimeSpan.FromSeconds(10), http.RequestAborted))
                            return Results.File(source, originalType, lastModified: info.LastWriteTimeUtc);
                        try
                        {
                            var bytes = Resize(source, width, format, quality);
                            if (bytes is null)
                                return Results.File(source, originalType, lastModified: info.LastWriteTimeUtc);

                            Directory.CreateDirectory(cacheDir);
                            var temp = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            await File.WriteAllBytesAsync(temp, bytes, http.RequestAborted);
                            File.Move(temp, cached, overwrite: true);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            loggerFactory.CreateLogger("Fratak.Media").LogWarning(exception, "Caching resized image {Path} failed", path);
                            return Results.File(source, originalType, lastModified: info.LastWriteTimeUtc);
                        }
                        finally
                        {
                            Workers.Release();
                        }
                    }
                }
                finally
                {
                    gate.Release();
                    Locks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(key, gate));
                }
            }

            return Results.File(cached, contentType, lastModified: info.LastWriteTimeUtc, entityTag: new EntityTagHeaderValue($"\"{key}\""));
        });
    }

    /// <summary>
    /// Decodes, downsizes (never upsizes) and re-encodes without metadata. Null = can't handle (the original is served).
    /// <para>مرحلهٔ هفدهم: هیچ خطایی بیرون نمی‌رود (عکس خراب/عجیب فقط یعنی «اصل عکس را بفرست»).</para>
    /// </summary>
    public static byte[]? Resize(string source, int width, SKEncodedImageFormat format, int quality)
    {
        try
        {
            return ResizeCore(source, width, format, quality);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// اندازهٔ رمزگشایی: اگر عکس خیلی بزرگ‌تر از عرض خواسته است، رمزگشا خودش آن را کوچک‌تر باز می‌کند (JPEG تا ۱/۸، WebP هر
    /// اندازه) و حافظهٔ لازم ۴ تا ۶۴ برابر کم می‌شود. مثال: عکس ۶۰۰۰×۴۰۰۰ برای عرض ۴۸۰ ← باز شدن در ۷۵۰×۵۰۰ (۱/۸) به جای ۹۶ مگابایت،
    /// حدود ۱٫۵ مگابایت. نتیجه هرگز از عرض خواسته کوچک‌تر نمی‌شود (کیفیت حفظ می‌شود). null = همان اندازهٔ اصلی.
    /// </summary>
    public static SKSizeI? DecodeSize(SKCodec codec, int width)
    {
        var info = codec.Info;
        var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var displayWidth = swap ? info.Height : info.Width;
        if (displayWidth <= width)
            return null;
        var scaled = codec.GetScaledDimensions((float)width / displayWidth);
        var scaledWidth = swap ? scaled.Height : scaled.Width;
        if (scaled.Width <= 0 || scaled.Height <= 0 || scaledWidth < width || (scaled.Width == info.Width && scaled.Height == info.Height))
            return null;
        return scaled;
    }

    private static byte[]? ResizeCore(string source, int width, SKEncodedImageFormat format, int quality)
    {
        using var codec = SKCodec.Create(source);
        if (codec is null)
            return null;

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > PixelLimit)
            return null;

        SKBitmap? scaledDecode = null;
        if (DecodeSize(codec, width) is { } size)
        {
            var target = info.WithSize(size.Width, size.Height);
            if (target.AlphaType == SKAlphaType.Unpremul)
                target.AlphaType = SKAlphaType.Premul;
            scaledDecode = SKBitmap.Decode(codec, target);
        }

        using var original = scaledDecode ?? SKBitmap.Decode(codec);
        if (original is null)
            return null;

        using var oriented = Orient(original, codec.EncodedOrigin);
        var bitmap = oriented ?? original;

        SKBitmap? resized = null;
        try
        {
            if (bitmap.Width > width)
            {
                var height = Math.Max(1, (int)Math.Round(bitmap.Height * (width / (double)bitmap.Width)));
                resized = bitmap.Resize(new SKImageInfo(width, height, bitmap.ColorType, bitmap.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell));
                if (resized is null)
                    return null;
            }

            using var image = SKImage.FromBitmap(resized ?? bitmap);
            using var data = image.Encode(format, quality);
            return data?.ToArray();
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static SKBitmap? Orient(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
            return null;

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height);
        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, bitmap.Width / 2f, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, bitmap.Width / 2f, bitmap.Height / 2f); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, bitmap.Height / 2f); break;
            case SKEncodedOrigin.LeftTop: canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); canvas.Scale(1, -1, 0, bitmap.Height / 2f); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(0, result.Height); canvas.RotateDegrees(270); canvas.Scale(1, -1, 0, bitmap.Height / 2f); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, result.Height); canvas.RotateDegrees(270); break;
        }
        canvas.DrawBitmap(bitmap, 0, 0);
        return result;
    }

    private static string ContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".webp" => "image/webp",
        ".png" => "image/png",
        _ => "image/jpeg",
    };
}
