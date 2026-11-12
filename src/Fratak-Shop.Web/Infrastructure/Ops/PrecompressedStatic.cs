using System.Collections.Concurrent;
using System.IO.Compression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace Fratak_Shop.Web.Infrastructure.Ops;

/// <summary>
/// دور نوزدهم (A17): فایل‌های ایستای متنی (CSS، JS، SVG، JSON) یک بار با بالاترین فشرده‌سازی (Brotli/Gzip) فشرده و در حافظه نگه
/// داشته می‌شوند؛ درخواست‌های بعدی بدون هیچ پردازشی همان بایت‌ها را می‌گیرند.
/// <para>قبلاً میان‌افزار فشرده‌سازی هر بار (برای هر بازدیدکننده) فایل را با سطح «سریع» دوباره فشرده می‌کرد: هم CPU بیشتر، هم
/// فایل بزرگ‌تر. حالا: فشرده‌سازی فقط اولین بار، حجم ۱۵ تا ۲۵٪ کمتر، صفر CPU برای بقیه.</para>
/// <list type="bullet">
/// <item>فقط زیر <c>/assets/</c> و پسوندهای متنی؛ تصویرها (WebP) از قبل فشرده‌اند و دست نمی‌خورند.</item>
/// <item>اگر فایل عوض شود (تاریخ تغییر/حجم)، نسخهٔ فشردهٔ جدید ساخته می‌شود.</item>
/// <item>سقف کل حافظه <see cref="MaxCacheBytes"/>؛ بیشتر شد، فایل جدید عادی (با فشرده‌سازی معمول) ارسال می‌شود.</item>
/// <item>درخواست Range، HEAD یا بدون Accept-Encoding مناسب ← مسیر معمول فایل‌های ایستا.</item>
/// </list>
/// <para>جای قرارگیری: Program.cs درست پیش از UseStaticFiles. مثال: GET /assets/css/main.css?v=abc با «Accept-Encoding: br» ← بایت‌های Brotli.</para>
/// </summary>
public sealed class PrecompressedStaticFilesMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    /// <summary>سقف کل حافظهٔ نسخه‌های فشرده (۱۶ مگابایت).</summary>
    public const long MaxCacheBytes = 16L * 1024 * 1024;

    /// <summary>فایل‌های بزرگ‌تر از این (۲ مگ) در حافظه نگه داشته نمی‌شوند.</summary>
    public const long MaxFileBytes = 2L * 1024 * 1024;

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".css", ".js", ".svg", ".json", ".webmanifest", ".txt", ".xml", ".map" };
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private static readonly ConcurrentDictionary<string, Entry> Cache = new(StringComparer.Ordinal);
    private static long _cachedBytes;

    private sealed record Entry(DateTime LastWrite, long Length, byte[] Body);

    public async Task InvokeAsync(HttpContext http)
    {
        var request = http.Request;
        var path = request.Path.Value;
        if (!HttpMethods.IsGet(request.Method) || path is null || !path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
            || request.Headers.ContainsKey(HeaderNames.Range) || !Extensions.Contains(Path.GetExtension(path)) || path.Contains("..", StringComparison.Ordinal))
        {
            await next(http);
            return;
        }

        var encoding = ChooseEncoding(request.Headers.AcceptEncoding.ToString());
        if (encoding is null)
        {
            await next(http);
            return;
        }

        var file = environment.WebRootFileProvider.GetFileInfo(path);
        if (!file.Exists || file.IsDirectory || file.Length > MaxFileBytes || !ContentTypes.TryGetContentType(path, out var contentType))
        {
            await next(http);
            return;
        }

        // درخواست شرطی مرورگر (If-Modified-Since): فایل عوض نشده ← ۳۰۴ بدون بدنه.
        if (DateTimeOffset.TryParse(request.Headers.IfModifiedSince.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var since)
            && file.LastModified.ToUnixTimeSeconds() <= since.ToUnixTimeSeconds())
        {
            http.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        var key = encoding + "|" + path.ToLowerInvariant();
        var lastWrite = file.LastModified.UtcDateTime;
        if (!Cache.TryGetValue(key, out var entry) || entry.LastWrite != lastWrite || entry.Length != file.Length)
        {
            if (Interlocked.Read(ref _cachedBytes) > MaxCacheBytes)
            {
                await next(http);
                return;
            }
            await using var input = file.CreateReadStream();
            var body = await CompressAsync(input, encoding, http.RequestAborted);
            entry = new Entry(lastWrite, file.Length, body);
            Cache[key] = entry;
            Interlocked.Add(ref _cachedBytes, body.Length);
        }

        var response = http.Response;
        response.ContentType = contentType.StartsWith("text/", StringComparison.Ordinal) || contentType.Contains("javascript", StringComparison.Ordinal)
                               || contentType.Contains("json", StringComparison.Ordinal) || contentType.Contains("svg", StringComparison.Ordinal)
            ? contentType + "; charset=utf-8"
            : contentType;
        response.Headers.ContentEncoding = encoding;
        response.Headers.Vary = "Accept-Encoding";
        response.Headers.CacheControl = request.Query.ContainsKey("v") ? "public, max-age=31536000, immutable" : "public, max-age=604800";
        response.Headers.LastModified = file.LastModified.ToString("R");
        response.ContentLength = entry.Body.Length;
        await response.Body.WriteAsync(entry.Body, http.RequestAborted);
    }

    /// <summary>
    /// بهترین فشرده‌سازی پذیرفته‌شده: br، بعد gzip؛ «q=0» یعنی نپذیرفته. مثال: «gzip, deflate, br» → «br»؛ «gzip» → «gzip»؛ «identity» → null.
    /// </summary>
    public static string? ChooseEncoding(string? acceptEncoding)
    {
        if (string.IsNullOrWhiteSpace(acceptEncoding))
            return null;
        bool Accepts(string name) => acceptEncoding.Split(',').Select(part => part.Trim()).Any(part =>
        {
            var pieces = part.Split(';');
            if (!pieces[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return false;
            return !pieces.Skip(1).Any(p => p.Replace(" ", string.Empty).Equals("q=0", StringComparison.OrdinalIgnoreCase)
                                             || p.Replace(" ", string.Empty).Equals("q=0.0", StringComparison.OrdinalIgnoreCase));
        });
        if (Accepts("br"))
            return "br";
        return Accepts("gzip") ? "gzip" : null;
    }

    /// <summary>فشرده‌سازی با بالاترین سطح (فقط یک بار برای هر فایل). مثال: <c>await CompressAsync(stream, "br", ct)</c>.</summary>
    public static async Task<byte[]> CompressAsync(Stream input, string encoding, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        if (encoding == "br")
        {
            await using var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true);
            await input.CopyToAsync(brotli, cancellationToken);
        }
        else
        {
            await using var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true);
            await input.CopyToAsync(gzip, cancellationToken);
        }
        return output.ToArray();
    }

    /// <summary>فقط آزمون‌ها.</summary>
    public static void ResetForTests()
    {
        Cache.Clear();
        Interlocked.Exchange(ref _cachedBytes, 0);
    }
}
