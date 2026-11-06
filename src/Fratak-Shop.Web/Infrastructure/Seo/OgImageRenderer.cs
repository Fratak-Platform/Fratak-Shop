using System.Security.Cryptography;
using System.Text;
using Fratak_Shop.Web.Infrastructure.Media;
using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Seo;

/// <summary>
/// تصویر اشتراک‌گذاری (Open Graph) خودکار ۱۲۰۰×۶۳۰ برای هر صفحه (موارد ۱۳۳ و ۲۰۱ پیشنهاد سئو).
/// <para>چرا؟ عکس محصول معمولاً مربعی است؛ تلگرام، واتس‌اپ، فیسبوک و X آن را در کادر ۱.۹۱:۱ می‌بُرند و نیمی از محصول دیده
/// نمی‌شود. این کلاس عکس را کامل وسط کادر استاندارد می‌گذارد و پس‌زمینه را با نسخهٔ محو و روشن همان عکس پر می‌کند.</para>
/// <para>خروجی WebP (سیاست «فقط WebP») و روی دیسک کش می‌شود (کلید: مسیر + زمان تغییر فایل).</para>
/// <para>مسیر: <c>/media/og/{path}</c>؛ آدرس را <see cref="Url"/> می‌سازد و <c>PageMetaService</c> برای og:image استفاده می‌کند.</para>
/// </summary>
public sealed class OgImageRenderer(IWebHostEnvironment environment, ILogger<OgImageRenderer> logger)
{
    public const int Width = 1200;
    public const int Height = 630;
    private static readonly SemaphoreSlim Workers = new(2, 2);

    /// <summary>
    /// آدرس تصویر اشتراک‌گذاری برای یک تصویر محلی؛ بیرونی/نامعتبر ← همان آدرس.
    /// مثال: «/uploads/products/a.webp» ← «/media/og/uploads/products/a.webp».
    /// </summary>
    public static string Url(string imageUrl) =>
        MediaEndpoints.IsOptimizable(imageUrl) ? "/media/og/" + imageUrl.Split('?', '#')[0].TrimStart('/') : imageUrl;

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapMethods("/media/og/{**path}", [HttpMethods.Get, HttpMethods.Head], async (string path, HttpContext http, OgImageRenderer renderer) =>
        {
            var file = await renderer.RenderAsync(path, http.RequestAborted);
            if (file is null)
                return Results.NotFound();
            http.Response.Headers.CacheControl = "public, max-age=604800";
            return Results.File(file, "image/webp", lastModified: File.GetLastWriteTimeUtc(file));
        });

    /// <summary>مسیر فایل کش‌شده (در صورت نبود ساخته می‌شود)؛ نامعتبر ← null.</summary>
    public async Task<string?> RenderAsync(string path, CancellationToken cancellationToken)
    {
        if (!MediaEndpoints.IsOptimizable("/" + path))
            return null;
        var webRoot = Path.GetFullPath(environment.WebRootPath);
        var source = Path.GetFullPath(Path.Combine(webRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!source.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(source))
            return null;
        var info = new FileInfo(source);
        if (info.Length > 20 * 1024 * 1024)
            return null;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"og|{path.ToLowerInvariant()}|{info.LastWriteTimeUtc.Ticks}|{info.Length}")))[..32].ToLowerInvariant();
        var folder = Path.Combine(environment.ContentRootPath, "App_Data", "media-cache", key[..2]);
        var cached = Path.Combine(folder, key + ".og.webp");
        if (File.Exists(cached))
            return cached;
        await Workers.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(cached))
                return cached;
            var bytes = Render(await File.ReadAllBytesAsync(source, cancellationToken));
            if (bytes is null)
                return null;
            Directory.CreateDirectory(folder);
            var temp = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
            File.Move(temp, cached, overwrite: true);
            return cached;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "OG image for {Path} could not be cached", path);
            return null;
        }
        finally
        {
            Workers.Release();
        }
    }

    /// <summary>
    /// ساخت تصویر ۱۲۰۰×۶۳۰ (ایستا برای تست). تصویر نامعتبر ← null.
    /// پس‌زمینه: همان عکس، کشیده تا کادر پر شود، محو (sigma 40) و ۷۰٪ روشن‌شده؛ روی آن عکس کامل با حاشیهٔ ۴۰ پیکسل.
    /// </summary>
    public static byte[]? Render(byte[] data)
    {
        try
        {
            using var decoded = SKBitmap.Decode(data);
            if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
                return null;
            using var image = SKImage.FromBitmap(decoded);
            using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);
            var sampling = new SKSamplingOptions(SKCubicResampler.Mitchell);

            // پس‌زمینه (cover + blur + روشن)
            var cover = Math.Max(Width / (double)decoded.Width, Height / (double)decoded.Height);
            var coverWidth = (float)(decoded.Width * cover);
            var coverHeight = (float)(decoded.Height * cover);
            using (var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(40, 40) })
            {
                canvas.DrawImage(image, new SKRect((Width - coverWidth) / 2, (Height - coverHeight) / 2, (Width + coverWidth) / 2, (Height + coverHeight) / 2), sampling, blur);
            }

            using (var veil = new SKPaint { Color = new SKColor(255, 255, 255, 178) })
                canvas.DrawRect(0, 0, Width, Height, veil);

            // عکس اصلی (contain)
            const int margin = 40;
            var contain = Math.Min((Width - 2 * margin) / (double)decoded.Width, (Height - 2 * margin) / (double)decoded.Height);
            contain = Math.Min(contain, 2.5); // عکس خیلی کوچک بیش از حد بزرگ (و تار) نشود
            var w = (float)(decoded.Width * contain);
            var h = (float)(decoded.Height * contain);
            canvas.DrawImage(image, new SKRect((Width - w) / 2, (Height - h) / 2, (Width + w) / 2, (Height + h) / 2), sampling);

            using var snapshot = surface.Snapshot();
            using var encoded = snapshot.Encode(SKEncodedImageFormat.Webp, WebPEncoder.Quality);
            return encoded?.ToArray();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }
}
