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

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly SemaphoreSlim Workers = new(Math.Max(1, Environment.ProcessorCount / 2));

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

    /// <summary>اندازه‌های مجاز آیکون PNG لحظه‌ای (iOS، ایمیل، PWA).</summary>
    public static readonly int[] IconSizes = [32, 48, 64, 96, 128, 144, 152, 167, 180, 192, 256, 512];

    /// <summary>
    /// آدرس PNG برای تصویر WebP در جاهایی که WebP پشتیبانی نمی‌شود (آیکون خانهٔ آیفون، لوگوی ایمیل در Outlook).
    /// غیر WebP یا بیرونی ← همان آدرس. مثال: <c>RasterIcon("/uploads/media/a.webp", 180)</c> ← «/media/icon/180/uploads/media/a.webp».
    /// </summary>
    public static string RasterIcon(string url, int size)
    {
        if (!IsOptimizable(url) || !url.Split('?', '#')[0].EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            return url;
        var fit = IconSizes.FirstOrDefault(s => s >= size, IconSizes[^1]);
        return $"/media/icon/{fit}/{url.Split('?', '#')[0].TrimStart('/')}";
    }

    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        // دور سئوی خودکار: همهٔ تصویرها WebP اند؛ این مسیر برای iOS/ایمیل یک PNG مربعی (کش روی دیسک) می‌سازد.
        app.MapMethods("/media/icon/{size:int}/{**path}", [HttpMethods.Get, HttpMethods.Head], async (int size, string path, HttpContext http,
            IWebHostEnvironment environment) =>
        {
            if (Array.IndexOf(IconSizes, size) < 0 || !IsOptimizable("/" + path))
                return Results.NotFound();
            var webRoot = Path.GetFullPath(environment.WebRootPath);
            var source = Path.GetFullPath(Path.Combine(webRoot, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!source.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(source))
                return Results.NotFound();
            var info = new FileInfo(source);
            if (info.Length > MaxSourceBytes)
                return Results.NotFound();
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"icon|{path.ToLowerInvariant()}|{size}|{info.LastWriteTimeUtc.Ticks}|{info.Length}")))[..32].ToLowerInvariant();
            var cacheDir = Path.Combine(environment.ContentRootPath, "App_Data", "media-cache", key[..2]);
            var cached = Path.Combine(cacheDir, key + ".png");
            http.Response.Headers.CacheControl = "public, max-age=604800";
            if (!File.Exists(cached))
            {
                await Workers.WaitAsync(http.RequestAborted);
                try
                {
                    var png = WebPEncoder.ToPng(await File.ReadAllBytesAsync(source, http.RequestAborted), size);
                    if (png is null)
                        return Results.NotFound();
                    Directory.CreateDirectory(cacheDir);
                    var temp = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    await File.WriteAllBytesAsync(temp, png, http.RequestAborted);
                    File.Move(temp, cached, overwrite: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return Results.NotFound();
                }
                finally
                {
                    Workers.Release();
                }
            }

            return Results.File(cached, "image/png", lastModified: info.LastWriteTimeUtc, entityTag: new EntityTagHeaderValue($"\"{key}\""));
        });

        app.MapMethods("/media/{width:int}/{**path}", [HttpMethods.Get, HttpMethods.Head], async (int width, string path, HttpContext http,
            IWebHostEnvironment environment, IStoreSettingService settingService, ILoggerFactory loggerFactory,
            Fratak_Shop.Core.Services.Smart.ISmartConfigService smartConfig) =>
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
            if (!settings.IsImageOptimizationEnabled || info.Length > MaxSourceBytes)
                return Results.File(source, originalType, lastModified: info.LastWriteTimeUtc, enableRangeProcessing: false);

            var acceptsWebp = http.Request.Headers.Accept.ToString().Contains("image/webp", StringComparison.OrdinalIgnoreCase);
            var format = acceptsWebp ? SKEncodedImageFormat.Webp
                : info.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ? SKEncodedImageFormat.Png
                : SKEncodedImageFormat.Jpeg;
            var quality = Math.Clamp(settings.ImageQuality, 40, 95);

            // دور ۲۹ (#۷): لوگوی کم‌رنگ فروشگاه روی عکس کالاها (اگر مدیر روشن کرده). تنظیمات لوگو در کلید کش هستند ←
            // با تغییر جا/پیدایی/لوگو نسخهٔ تازه ساخته می‌شود و نسخهٔ قبلی دیگر استفاده نمی‌شود.
            var overlay = LogoOverlay.For(await smartConfig.GetAsync(http.RequestAborted), settings, path, width, webRoot);

            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{path.ToLowerInvariant()}|{width}|{quality}|{format}|{info.LastWriteTimeUtc.Ticks}|{info.Length}{overlay?.CacheKey}")))[..32].ToLowerInvariant();
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
                        await Workers.WaitAsync(http.RequestAborted);
                        try
                        {
                            var bytes = Resize(source, width, format, quality, overlay);
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

    /// <summary>Decodes, downsizes (never upsizes) and re-encodes without metadata. Null = can't handle.</summary>
    internal static byte[]? Resize(string source, int width, SKEncodedImageFormat format, int quality, LogoOverlay? overlay = null)
    {
        using var codec = SKCodec.Create(source);
        if (codec is null)
            return null;

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxSourcePixels)
            return null;

        using var original = SKBitmap.Decode(codec);
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

            overlay?.Draw(resized ?? bitmap);
            // دور ۲۹: عکس اصلی دوربری‌شده (شفاف) برای مرورگر بدون WebP به JPEG می‌رود؛ JPEG شفافیت ندارد ← روی سفید (نه سیاه).
            using var flat = format == SKEncodedImageFormat.Jpeg && (resized ?? bitmap).AlphaType != SKAlphaType.Opaque ? OnWhite(resized ?? bitmap) : null;
            using var image = SKImage.FromBitmap(flat ?? resized ?? bitmap);
            using var data = image.Encode(format, quality);
            return data?.ToArray();
        }
        finally
        {
            resized?.Dispose();
        }
    }

    /// <summary>کپی تصویر روی زمینهٔ سفید (برای JPEG). مثال: PNG شفاف ← همان تصویر با زمینهٔ سفید.</summary>
    internal static SKBitmap OnWhite(SKBitmap source)
    {
        var result = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return result;
    }

    /// <summary>اعمال جهت EXIF (عکس موبایل). جهت عادی ← null. استفاده: Resize و WebPEncoder.</summary>
    internal static SKBitmap? Orient(SKBitmap bitmap, SKEncodedOrigin origin)
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

/// <summary>
/// دور ۲۹ (#۷) – لوگوی کم‌رنگ فروشگاه روی نسخه‌های نمایشی عکس کالا (/media/{width}/uploads/products/…).
/// <list type="bullet">
/// <item>فقط عکس کالا و فقط عرض ۳۲۰ به بالا (بندانگشتی‌های ۱۶۰ پیکسلی شلوغ نمی‌شوند)؛ فایل اصلی عکس هیچ‌وقت تغییر نمی‌کند.</item>
/// <item>لوگو: «mark» ← آیکون برند (PWA)، نبود ← لوگوی اصلی؛ «full» ← لوگوی اصلی. فقط فایل محلی همین سایت.</item>
/// <item>اندازه: نشان ۱۴٪ و لوگوی کامل ۲۴٪ عرض عکس، با فاصلهٔ ۳٪ از لبه؛ پیدایی ۳ تا ۳۰ درصد (پیش‌فرض ۸ = خیلی کم‌رنگ).</item>
/// </list>
/// </summary>
public sealed class LogoOverlay
{
    private LogoOverlay(string logoPath, string position, int opacity, bool full, string cacheKey)
    {
        LogoPath = logoPath;
        Position = position;
        Opacity = opacity;
        Full = full;
        CacheKey = cacheKey;
    }

    /// <summary>مسیر کامل فایل لوگو روی دیسک.</summary>
    public string LogoPath { get; }

    /// <summary>جای لوگو: br، bl، tr، tl یا c.</summary>
    public string Position { get; }

    /// <summary>درصد پیدایی (۳ تا ۳۰).</summary>
    public int Opacity { get; }

    /// <summary>لوگوی کامل افقی (true) یا نشان مربعی (false).</summary>
    public bool Full { get; }

    /// <summary>بخشی که به کلید کش اضافه می‌شود (لوگو + زمان تغییرش + جا + پیدایی).</summary>
    public string CacheKey { get; }

    /// <summary>
    /// تنظیم لوگو برای این درخواست؛ خاموش، عکس غیر کالا، عرض کم یا لوگوی ناموجود ← null.
    /// مثال: (روشن، «uploads/products/a.webp»، ۶۴۰) ← لوگو پایین راست با پیدایی ۸٪.
    /// </summary>
    public static LogoOverlay? For(Fratak_Shop.Core.Services.Smart.SmartOptions smart, Fratak_Shop.Core.DTOs.Settings.StoreSettingDto settings, string path, int width, string webRoot)
    {
        if (!smart.ImageLogoOverlay || width < 320 || !path.StartsWith("uploads/products/", StringComparison.OrdinalIgnoreCase))
            return null;
        var full = smart.ImageLogoKind == "full";
        var url = full ? settings.LogoUrl : (string.IsNullOrWhiteSpace(settings.PwaIconUrl) ? settings.LogoUrl : settings.PwaIconUrl);
        if (string.IsNullOrWhiteSpace(url) || !MediaEndpoints.IsOptimizable(url.Trim()))
            return null;
        var relative = url.Trim().Split('?', '#')[0].TrimStart('/');
        var file = Path.GetFullPath(Path.Combine(webRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!file.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(file))
            return null;
        var stamp = File.GetLastWriteTimeUtc(file).Ticks;
        return new LogoOverlay(file, smart.ImageLogoPosition, smart.ImageLogoOpacity, full,
            $"|logo:{relative.ToLowerInvariant()}:{stamp}:{smart.ImageLogoPosition}:{smart.ImageLogoOpacity}:{(full ? "f" : "m")}");
    }

    /// <summary>لوگو را با پیدایی کم روی همان تصویر می‌کشد. لوگوی خراب ← بدون تغییر (عکس هرگز به خاطر لوگو خراب نمی‌شود).</summary>
    public void Draw(SKBitmap target)
    {
        using var logo = SKBitmap.Decode(LogoPath);
        if (logo is null || logo.Width <= 0 || logo.Height <= 0 || target.Width < 64)
            return;
        var w = Math.Max(24f, target.Width * (Full ? 0.24f : 0.14f));
        var h = w * logo.Height / logo.Width;
        if (h > target.Height * 0.3f)
        {
            h = target.Height * 0.3f;
            w = h * logo.Width / logo.Height;
        }
        var margin = target.Width * 0.03f;
        var x = Position is "bl" or "tl" ? margin : Position == "c" ? (target.Width - w) / 2 : target.Width - w - margin;
        var y = Position is "tr" or "tl" ? margin : Position == "c" ? (target.Height - h) / 2 : target.Height - h - margin;
        using var canvas = new SKCanvas(target);
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Clamp(Opacity * 255 / 100, 8, 80)) };
        using var image = SKImage.FromBitmap(logo);
        canvas.DrawImage(image, SKRect.Create(x, y, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        canvas.Flush();
    }
}
