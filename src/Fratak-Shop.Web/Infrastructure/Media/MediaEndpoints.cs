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
            if (!settings.IsImageOptimizationEnabled || info.Length > MaxSourceBytes)
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
                        await Workers.WaitAsync(http.RequestAborted);
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

    /// <summary>Decodes, downsizes (never upsizes) and re-encodes without metadata. Null = can't handle.</summary>
    internal static byte[]? Resize(string source, int width, SKEncodedImageFormat format, int quality)
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

            using var image = SKImage.FromBitmap(resized ?? bitmap);
            using var data = image.Encode(format, quality);
            return data?.ToArray();
        }
        finally
        {
            resized?.Dispose();
        }
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
