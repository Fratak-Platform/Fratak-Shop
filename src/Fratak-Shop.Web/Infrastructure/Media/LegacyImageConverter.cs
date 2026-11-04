using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Autopilot;

namespace Fratak_Shop.Web.Infrastructure.Media;

/// <summary>نتیجهٔ یک دور تبدیل تصویرهای قدیمی.</summary>
/// <param name="Converted">تعداد فایل‌های تبدیل‌شده.</param>
/// <param name="Failed">تعداد فایل‌هایی که تبدیل نشدند (فایل اصلی دست‌نخورده ماند).</param>
/// <param name="RowsUpdated">تعداد ردیف‌های دیتابیس که ارجاعشان اصلاح شد.</param>
/// <param name="SavedBytes">حجم صرفه‌جویی‌شده (بایت).</param>
public sealed record LegacyImageResult(int Converted, int Failed, int RowsUpdated, long SavedBytes)
{
    public string Message => Converted == 0 && Failed == 0 && RowsUpdated == 0
        ? "تصویر JPG/PNG/GIF قدیمی‌ای نماند."
        : $"{Converted} تصویر به WebP تبدیل شد، {RowsUpdated} ارجاع اصلاح شد، {SavedBytes / 1024:N0} کیلوبایت کمتر"
          + (Failed > 0 ? $" ({Failed} فایل تبدیل نشد و دست‌نخورده ماند)." : ".");
}

/// <summary>
/// تبدیل تصویرهای قدیمی پوشهٔ <c>wwwroot/uploads</c> به WebP (دور سئوی خودکار – «تصویرهای فعلی هم تبدیل شوند»).
/// <para>مراحل (ایمن و بدون خرابی):
/// ۱) هر JPG/PNG/GIF/BMP با همان نام و پسوند ‎.webp ساخته می‌شود (فایل موقت ← جابه‌جایی اتمیک).
/// ۲) همهٔ ارجاع‌های دیتابیس (نام فایل در ستون‌ها، آدرس داخل HTML متن‌ها، تنظیمات) با
/// <see cref="ISeoMaintenanceService.ReplaceImageReferencesAsync"/> اصلاح می‌شوند. ارجاع تصویرهای قالب
/// (<c>/assets/image/…jpg</c>) که در مخزن به WebP تبدیل شده‌اند هم همین‌جا اصلاح می‌شوند.
/// ۳) فقط بعد از اصلاح دیتابیس، فایل‌های اصلی پاک می‌شوند. اگر جایی هنوز آدرس قدیمی باشد (مثلاً لینک بیرونی)،
/// <see cref="LegacyImageRedirectMiddleware"/> با ۳۰۱ به نسخهٔ WebP می‌برد.</para>
/// <para>کجا استفاده می‌شود؟ کارگر خلبان خودکار (بار اول بعد از انتشار و هر شب) و دکمهٔ «تبدیل تصویرها» در پنل.</para>
/// </summary>
public sealed partial class LegacyImageConverter(IWebHostEnvironment environment, ILogger<LegacyImageConverter> logger)
{
    private static readonly string[] Convertible = [".jpg", ".jpeg", ".png", ".gif", ".bmp"];

    /// <summary>بیشترین فایل در هر دور (بار سرور کنترل می‌شود؛ بقیه دور بعد).</summary>
    public const int MaxPerRun = 400;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>اجرای یک دور. هم‌زمان فقط یک دور اجرا می‌شود (دور دوم بلافاصله «هیچ» برمی‌گرداند).</summary>
    public async Task<LegacyImageResult> RunAsync(ISeoMaintenanceService maintenance, CancellationToken cancellationToken)
    {
        if (!await Gate.WaitAsync(0, cancellationToken))
            return new LegacyImageResult(0, 0, 0, 0);
        try
        {
            var map = BundledMap(environment.WebRootPath);
            var originals = new List<string>();
            int converted = 0, failed = 0;
            long saved = 0;
            var uploads = Path.Combine(environment.WebRootPath, "uploads");
            if (Directory.Exists(uploads))
            {
                foreach (var file in Directory.EnumerateFiles(uploads, "*.*", SearchOption.AllDirectories))
                {
                    if (converted + failed >= MaxPerRun)
                        break;
                    var extension = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.IndexOf(Convertible, extension) < 0)
                        continue;
                    cancellationToken.ThrowIfCancellationRequested();
                    var target = Path.ChangeExtension(file, ".webp");
                    try
                    {
                        var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                        if (!File.Exists(target))
                        {
                            var webp = WebPEncoder.ToWebP(bytes, logger, tryLossless: extension == ".png");
                            if (webp is null)
                            {
                                failed++;
                                continue;
                            }

                            var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            await File.WriteAllBytesAsync(temp, webp.Data, cancellationToken);
                            File.Move(temp, target, overwrite: false);
                            saved += Math.Max(0, bytes.Length - webp.Data.Length);
                        }

                        converted++;
                        originals.Add(file);
                        map[Path.GetFileName(file)] = Path.GetFileName(target);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        failed++;
                        logger.LogWarning(exception, "Legacy image {File} could not be converted", file);
                    }
                }
            }

            var rows = map.Count == 0 ? 0 : await maintenance.ReplaceImageReferencesAsync(map, cancellationToken);
            foreach (var file in originals)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception, "Legacy image {File} could not be deleted", file);
                }
            }

            if (converted > 0 || rows > 0)
                logger.LogInformation("Legacy images: {Converted} converted, {Rows} references updated", converted, rows);
            return new LegacyImageResult(converted, failed, rows, saved);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// نقشهٔ تصویرهای قالب که در مخزن WebP شده‌اند: «/assets/image/blog/1.jpg|.jpeg|.png|.gif» ← «/assets/image/blog/1.webp».
    /// فقط برای فایل‌هایی که نسخهٔ WebP واقعاً موجود است (دیتابیس قدیمی سایت هنوز آدرس JPG دارد).
    /// </summary>
    public static Dictionary<string, string> BundledMap(string webRoot)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var folder = Path.Combine(webRoot, "assets", "image");
        if (!Directory.Exists(folder))
            return map;
        foreach (var webp in Directory.EnumerateFiles(folder, "*.webp", SearchOption.AllDirectories))
        {
            var relative = "/" + Path.GetRelativePath(webRoot, webp).Replace('\\', '/');
            if (!SafeAsset().IsMatch(relative))
                continue;
            var stem = relative[..^".webp".Length];
            foreach (var old in new[] { ".jpg", ".jpeg", ".png", ".gif" })
                map[stem + old] = relative;
        }

        return map;
    }

    [GeneratedRegex(@"^/assets/image/[A-Za-z0-9_\-/]+\.webp$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeAsset();
}

/// <summary>
/// ریدایرکت دائمی (۳۰۱) آدرس‌های قدیمی تصویر به نسخهٔ WebP: درخواست
/// <c>/uploads/…/x.jpg</c> یا <c>/assets/image/…/x.png</c> که فایلش دیگر نیست، اگر <c>x.webp</c> موجود باشد.
/// <para>چرا؟ گوگل تصویرها را با آدرس قدیمی ایندکس کرده و سایت‌های دیگر ممکن است به آن لینک داده باشند؛ ۳۰۱ اعتبار را
/// منتقل می‌کند و تصویر شکسته نمی‌ماند.</para>
/// <para>کجا ثبت می‌شود؟ Program.cs پیش از فایل‌های ایستا.</para>
/// </summary>
public sealed class LegacyImageRedirectMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    public Task InvokeAsync(HttpContext context)
    {
        var target = Target(context.Request.Path.Value, environment.WebRootPath);
        if (target is null || !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            return next(context);
        context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
        context.Response.Headers.Location = target + context.Request.QueryString;
        context.Response.Headers.CacheControl = "public, max-age=86400";
        return Task.CompletedTask;
    }

    /// <summary>
    /// آدرس WebP جایگزین یا null. مثال: «/uploads/products/a.jpg» (فایل نیست، a.webp هست) ← «/uploads/products/a.webp».
    /// مسیرهای دارای «..» یا بیرون از دو پوشهٔ مجاز همیشه null.
    /// </summary>
    public static string? Target(string? path, string webRoot)
    {
        if (string.IsNullOrEmpty(path) || path.Contains("..", StringComparison.Ordinal) || path.Contains('\\'))
            return null;
        if (!path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("/assets/image/", StringComparison.OrdinalIgnoreCase))
            return null;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp"))
            return null;
        var root = Path.GetFullPath(webRoot);
        var physical = Path.GetFullPath(Path.Combine(root, Uri.UnescapeDataString(path.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar)));
        if (!physical.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || File.Exists(physical))
            return null;
        return File.Exists(Path.ChangeExtension(physical, ".webp")) ? path[..^extension.Length] + ".webp" : null;
    }
}
