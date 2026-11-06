using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Web.Infrastructure.Media;
using Fratak_Shop.Web.Services;
using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>اطلاعات یک تصویر دانلودشده: ابعاد واقعی و اثر انگشت dHash.</summary>
public sealed record ImageInfo(int Width, int Height, ulong Hash);

/// <summary>
/// تحلیل تصویر با SkiaSharp (B3): ابعاد و اثر انگشت ادراکی (dHash) برای حذف تصویرهای تکراری – حتی اگر اندازه یا فشرده‌سازی فرق کند.
/// <para>حافظه: تصویر با کوچک‌ترین مقیاس ممکن باز می‌شود (JPEG تا ۱/۸) و همه از دروازهٔ <see cref="WebPEncoder.Gate"/> می‌گذرند
/// تا روی هاست ضعیف هم‌زمان چند عکس بزرگ باز نشود (A6). تصویر خراب/خیلی بزرگ ← null.</para>
/// <para>محل استفاده: <see cref="SmartImageFinder"/>.</para>
/// </summary>
public static class SmartImageAnalyzer
{
    private const long MaxPixels = 40_000_000;

    public static ImageInfo? Analyze(byte[] data)
    {
        if (data.Length < 100 || !WebPEncoder.Gate.Wait(TimeSpan.FromSeconds(60)))
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
            var dims = codec.GetScaledDimensions(Math.Min(1f, 256f / Math.Max(info.Width, info.Height)));
            // PNG/WebP را نمی‌توان کوچک باز کرد؛ نسخهٔ کامل بیش از ۳۰۰۰ پیکسل برای «اثر انگشت» نمی‌ارزد.
            if (Math.Max(dims.Width, dims.Height) > 3000)
                return null;
            var scaledInfo = new SKImageInfo(dims.Width, dims.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(scaledInfo);
            var result = codec.GetPixels(scaledInfo, bitmap.GetPixels());
            if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
                return null;
            using var tiny = bitmap.Resize(new SKImageInfo(9, 8, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            if (tiny is null)
                return null;
            var gray = new byte[72];
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 9; x++)
                {
                    var c = tiny.GetPixel(x, y);
                    gray[y * 9 + x] = (byte)((c.Red * 299 + c.Green * 587 + c.Blue * 114) / 1000);
                }
            }
            return new ImageInfo(info.Width, info.Height, ImageHash.DHash(gray));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            WebPEncoder.Gate.Release();
        }
    }

    /// <summary>
    /// قانون پذیرش (B2): کوتاه‌ترین ضلع ≥ حداقل تنظیمات (پیش‌فرض ۶۰۰) و نسبت ابعاد بین ۰٫۴۵ و ۲٫۲
    /// (نوار تبلیغ و اسکرین‌شات‌های باریک رد می‌شوند). مثال: ۱۲۰۰×۱۲۰۰ ← قبول؛ ۱۶۰۰×۴۰۰ ← رد؛ ۴۰۰×۴۰۰ ← رد.
    /// </summary>
    public static bool IsAcceptable(ImageInfo info, int minSide)
    {
        var ratio = info.Width / (double)info.Height;
        return Math.Min(info.Width, info.Height) >= minSide && ratio is >= 0.45 and <= 2.2;
    }
}

/// <summary>
/// سازندهٔ بنر بدون متن (B6): ۱۶۰۰×۶۰۰ از تصویر کالا – پس‌زمینهٔ محو همان تصویر و خود کالا با سایهٔ نرم در سمت چپ
/// (سمت راست برای متن فارسی بنر که مدیر در پنل می‌نویسد خالی می‌ماند). متن روی تصویر نوشته نمی‌شود چون Skia بدون HarfBuzz
/// حروف فارسی را جدا جدا می‌کشد. خروجی WebP؛ بنر «غیرفعال» ذخیره می‌شود تا مدیر تأیید کند.
/// </summary>
public static class BannerGenerator
{
    public const int Width = 1600;
    public const int Height = 600;

    public static byte[]? Create(byte[] source)
    {
        if (!WebPEncoder.Gate.Wait(TimeSpan.FromSeconds(60)))
            return null;
        try
        {
            using var bitmap = SKBitmap.Decode(source);
            if (bitmap is null || bitmap.Width < 200 || bitmap.Height < 200 || (long)bitmap.Width * bitmap.Height > 12_000_000)
                return null;
            using var image = SKImage.FromBitmap(bitmap);
            using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (surface is null)
                return null;
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            // پس‌زمینه: تصویر «پوشاننده» با محوی زیاد و کمی روشن‌تر.
            var cover = Math.Max(Width / (float)bitmap.Width, Height / (float)bitmap.Height) * 1.15f;
            var bw = bitmap.Width * cover;
            var bh = bitmap.Height * cover;
            using (var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(48, 48) })
                canvas.DrawImage(image, SKRect.Create((Width - bw) / 2, (Height - bh) / 2, bw, bh), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), blur);
            using (var veil = new SKPaint { Color = new SKColor(255, 255, 255, 120) })
                canvas.DrawRect(0, 0, Width, Height, veil);

            // خود کالا: حداکثر ۵۰۰ پیکسل ارتفاع، وسط نیمهٔ چپ، با سایهٔ نرم.
            var fit = Math.Min(500f / bitmap.Height, 640f / bitmap.Width);
            var fw = bitmap.Width * fit;
            var fh = bitmap.Height * fit;
            var dest = SKRect.Create(Width * 0.27f - fw / 2, (Height - fh) / 2, fw, fh);
            using (var shadow = new SKPaint { ImageFilter = SKImageFilter.CreateDropShadow(0, 18, 24, 24, new SKColor(0, 0, 0, 70)) })
                canvas.DrawImage(image, dest, new SKSamplingOptions(SKCubicResampler.Mitchell), shadow);

            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(SKEncodedImageFormat.Webp, WebPEncoder.Quality);
            return data?.ToArray();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            WebPEncoder.Gate.Release();
        }
    }
}

/// <summary>یک تصویر ذخیره‌شده توسط یابنده. Trusted: از منبع مطمئن (همان کالا در دیجی‌کالا/تأمین‌کننده/باسلام).</summary>
public sealed record FoundImage(string FileName, int Width, int Height, string Source, bool Trusted);

/// <summary>
/// یابندهٔ تصویر کالا/دسته/مقاله (B1–B6).
/// <para>منابع به ترتیب اعتماد: ۱) همان کالا در دیجی‌کالا (تطبیق نام و عدد مدل)؛ ۲) og:image صفحهٔ تأمین‌کننده؛ ۳) باسلام با نام مشابه؛
/// ۴) جست‌وجوی تصویر بینگ. تصویرهای ۱ تا ۳ «مطمئن»اند و خودکار اضافه می‌شوند؛ نتایج بینگ فقط «پیشنهاد» می‌شوند تا مدیر تأیید کند
/// (ضد دادهٔ بی‌ربط).</para>
/// <para>کیفیت: حداکثر ۳۶ نامزد، ۳ دانلود هم‌زمان، هر فایل ≤ ۵ مگابایت، ضلع کوتاه ≥ ۶۰۰، حذف تکراری‌ها با dHash (هم با تصویرهای فعلی کالا)،
/// ذخیره با همان پاک‌سازی آپلود دستی (WebP، بدون متادیتا، نام تصادفی).</para>
/// </summary>
public sealed class SmartImageFinder(
    IHttpClientFactory httpFactory,
    IUploadStore uploads,
    SmartSources sources,
    ISmartConfigService config,
    ILogger<SmartImageFinder> logger)
{
    public const int MaxImageBytes = 5 * 1024 * 1024;

    public async Task<List<FoundImage>> FindForProductAsync(SmartProductSubject subject, int want, CancellationToken cancellationToken)
    {
        var options = await config.GetAsync(cancellationToken);
        var trusted = new List<(string Url, string Source)>();
        var hit = await sources.FindDigikalaAsync(subject, cancellationToken);
        if (hit is not null && await sources.DigikalaDetailsAsync(hit.Id, cancellationToken) is { } details)
            trusted.AddRange(details.Images.Select(u => (u, "digikala")));
        if (!string.IsNullOrWhiteSpace(subject.SupplierUrl))
        {
            var (_, og) = await sources.PageAsync(subject.SupplierUrl, cancellationToken);
            if (og is not null)
                trusted.Add((og, "supplier"));
        }
        trusted.AddRange((await sources.BasalamImagesAsync(subject.SearchQuery, cancellationToken)).Select(u => (u, "basalam")));

        var suggested = new List<(string Url, string Source)>();
        if (trusted.Count < want)
            suggested.AddRange((await sources.BingImagesAsync(subject.SearchQuery, cancellationToken)).Select(u => (u, "bing")));

        var existing = subject.ImageNames.Select(n => uploads.GetPath(UploadArea.Products, n)).Where(p => p is not null && File.Exists(p))
            .Select(p => SmartImageAnalyzer.Analyze(File.ReadAllBytes(p!))).Where(i => i is not null).Select(i => i!.Hash).ToList();

        var candidates = trusted.Select(t => (t.Url, t.Source, Trusted: true))
            .Concat(suggested.Select(s => (s.Url, s.Source, Trusted: false)))
            .DistinctBy(c => c.Url, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(options.MaxImageCandidates, 4, 36))
            .ToList();
        return await DownloadAndStoreAsync(candidates, existing, Math.Clamp(want, 1, 12), options.MinImageSide, UploadArea.Products, cancellationToken);
    }

    /// <summary>
    /// تصویر دسته/مقاله (B4/B5): بنر بدون متن از بهترین تصویر کالاهای همان دسته/مقاله. خروجی نام فایل در ناحیهٔ مقصد.
    /// </summary>
    public async Task<string?> BannerFromProductImageAsync(string productImageName, UploadArea target, CancellationToken cancellationToken)
    {
        var path = uploads.GetPath(UploadArea.Products, productImageName);
        if (path is null || !File.Exists(path))
            return null;
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var banner = BannerGenerator.Create(bytes);
        return banner is null ? null : await StoreAsync(banner, target, cancellationToken);
    }

    /// <summary>حذف فایل‌های پیشنهادی ردشده (فقط نام فایل معتبر داخل پوشهٔ محصولات).</summary>
    public void DeleteProductFiles(IEnumerable<string> names) => uploads.Delete(UploadArea.Products, names.Select(n => Path.GetFileName(n)).Where(n => !string.IsNullOrWhiteSpace(n)).ToList());

    private async Task<List<FoundImage>> DownloadAndStoreAsync(List<(string Url, string Source, bool Trusted)> candidates, List<ulong> existing,
        int want, int minSide, UploadArea area, CancellationToken cancellationToken)
    {
        var results = new List<FoundImage>();
        var hashes = new List<ulong>(existing);
        using var throttle = new SemaphoreSlim(3);
        var downloads = candidates.Select(async c =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                return (Candidate: c, Bytes: await DownloadAsync(c.Url, cancellationToken));
            }
            finally
            {
                throttle.Release();
            }
        }).ToList();

        foreach (var task in downloads)
        {
            var (candidate, bytes) = await task;
            if (bytes is null || results.Count(r => r.Trusted == candidate.Trusted) >= want)
                continue;
            var info = SmartImageAnalyzer.Analyze(bytes);
            if (info is null || !SmartImageAnalyzer.IsAcceptable(info, minSide) || hashes.Any(h => ImageHash.IsDuplicate(h, info.Hash)))
                continue;
            var name = await StoreAsync(bytes, area, cancellationToken);
            if (name is null)
                continue;
            hashes.Add(info.Hash);
            results.Add(new FoundImage(name, info.Width, info.Height, candidate.Source, candidate.Trusted));
        }
        return results;
    }

    private async Task<string?> StoreAsync(byte[] bytes, UploadArea area, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "image", "image");
        var (files, error) = await uploads.SaveAsync(area, [file], 1, MaxImageBytes / 1024, cancellationToken);
        if (error is not null || files.Count != 1)
        {
            logger.LogDebug("Smart image was not stored: {Error}", error);
            return null;
        }
        return files[0].FileName;
    }

    private async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("image/avif,image/webp,image/png,image/jpeg,image/*;q=0.8");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await httpFactory.CreateClient(Suppliers.SupplierHttp.ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageBytes)
                return null;
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not null && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && type != "application/octet-stream")
                return null;
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await source.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > MaxImageBytes)
                    return null;
                buffer.Write(chunk, 0, read);
            }
            return buffer.Length > 1000 ? buffer.ToArray() : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or Suppliers.SupplierHttp.BlockedAddressException
                                              || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
