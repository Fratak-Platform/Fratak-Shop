using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Web.Infrastructure.Media;
using Fratak_Shop.Web.Services;
using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>اطلاعات یک تصویر دانلودشده: ابعاد واقعی، اثر انگشت dHash و (دور ۲۴) امتیاز کیفیت ۰ تا ۱۰۰ (۰ = نامعلوم).</summary>
public sealed record ImageInfo(int Width, int Height, ulong Hash, int Quality = 0);

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
            return new ImageInfo(info.Width, info.Height, ImageHash.DHash(gray), QualityOf(bitmap, Math.Min(info.Width, info.Height)));
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
    /// دور ۲۴ (پیشنهاد ۵۸): امتیاز کیفیت از نسخهٔ کوچک‌شده (حداکثر ۲۵۶ پیکسل) با <see cref="Fratak_Shop.Core.Services.Smart.ImageQuality"/>.
    /// خطا ← ۰ (یعنی نامعلوم؛ عکس رد نمی‌شود).
    /// </summary>
    private static int QualityOf(SKBitmap bitmap, int originalMinSide)
    {
        try
        {
            // PNG/WebP کوچک باز نمی‌شوند (تا ۳۰۰۰ پیکسل)؛ برای سرعت و سنجش یکسان تیزی، همه به حداکثر ۲۵۶ پیکسل می‌رسند.
            var scale = Math.Min(1.0, 256.0 / Math.Max(bitmap.Width, bitmap.Height));
            using var small = scale < 1
                ? bitmap.Resize(new SKImageInfo(Math.Max(3, (int)(bitmap.Width * scale)), Math.Max(3, (int)(bitmap.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None))
                : null;
            var source = small ?? bitmap;
            int w = source.Width, h = source.Height;
            if (w < 3 || h < 3)
                return 0;
            var gray = new byte[w * h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var c = source.GetPixel(x, y);
                    // پیکسل شفاف (PNG) روی زمینهٔ سفید دیده می‌شود.
                    var a = c.Alpha / 255.0;
                    var lum = (c.Red * 299 + c.Green * 587 + c.Blue * 114) / 1000.0;
                    gray[y * w + x] = (byte)Math.Clamp(Math.Round(lum * a + 255 * (1 - a)), 0, 255);
                }
            }
            return Fratak_Shop.Core.Services.Smart.ImageQuality.Score(gray, w, h, originalMinSide).Total;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return 0;
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

/// <summary>
/// یک تصویر ذخیره‌شده توسط یابنده. Trusted: از منبع «همان کالا» (دیجی‌کالا/تأمین‌کننده/باسلام).
/// <para>دور بیستم (همه اختیاری تا نتیجه‌های قدیمی صف هم خوانده شوند): Url = آدرس اصلی تصویر (برای فهرست سیاه)، Hash = dHash،
/// PageUrl = صفحه‌ای که تصویر در آن بود، Title = عنوان آن صفحه/کارت، Score = امتیاز رتبه‌بندی (۰ تا ۱).</para>
/// </summary>
/// <para>دور ۲۳: Model = نتیجهٔ بررسی خودکار مدل منبع (<see cref="ImageModelCheck"/>)؛ نتیجه‌های قدیمی ← Unknown.</para>
public sealed record FoundImage(string FileName, int Width, int Height, string Source, bool Trusted,
    string? Url = null, long Hash = 0, string? PageUrl = null, string? Title = null, double Score = 0, ModelVerdict Model = ModelVerdict.Unknown, int Quality = 0);

/// <summary>یک نامزد تصویر پیش از دانلود (داخلی یابنده و آزمون‌ها).</summary>
/// <remarks>دور ۲۳: Model = بررسی خودکار مدل منبع؛ «همان مدل» ۰٫۰۵ امتیاز بیشتر می‌گیرد (نامزد «مدل دیگر» اصلاً ساخته نمی‌شود).</remarks>
public sealed record ImageCandidate(string Url, string Source, bool Trusted, double Match, string? Title = null, string? PageUrl = null,
    ModelVerdict Model = ModelVerdict.Unknown)
{
    /// <summary>
    /// امتیاز رتبه‌بندی: ۶۰٪ سابقهٔ تأیید منبع (از تصمیم‌های کارشناس) + ۴۰٪ شباهت عنوان به کالای ما + ۰٫۱ برای منبع «همان کالا».
    /// مثال: دیجی‌کالا با سابقهٔ ۰٫۸ و شباهت ۱ ← ۰٫۹۸؛ بینگ ناشناس (۰٫۴۵) با شباهت ۰٫۵ ← ۰٫۴۷.
    /// </summary>
    public double Score(IReadOnlyDictionary<string, double>? sourceScores) =>
        Math.Round(SmartImageRules.ScoreOf(Source, sourceScores) * 0.6 + Math.Clamp(Match, 0, 1) * 0.4 + (Trusted ? 0.1 : 0)
                   + (Model == ModelVerdict.Same ? 0.05 : 0), 4);
}

/// <summary>
/// یابندهٔ تصویر کالا/دسته/مقاله (B1–B6، گسترش دور بیستم).
/// <para>منابع: ۱) همان کالا در دیجی‌کالا (تطبیق نام و عدد مدل)؛ ۲) صفحهٔ تأمین‌کننده (og:image + JSON-LD)؛ ۳) باسلام با نام مشابه؛
/// ۴) فروشگاه‌های نتایج جست‌وجو (عنوان با شباهت ≥ ۰٫۶، تصویرهای og و JSON-LD هر صفحه)؛ ۵) جست‌وجوی تصویر بینگ با عنوان کارت
/// (شباهت ≥ ۰٫۵؛ کارت بدون عنوان کنار گذاشته می‌شود).</para>
/// <para>ضد عکس اشتباه: هر عکسی که کارشناس برای همین کالا رد کرده (آدرس یا اثر انگشت تصویری) هرگز دوباره نمی‌آید؛ منابع بر اساس
/// نسبت تأیید قبلی کارشناس مرتب می‌شوند؛ «جست‌وجوی دوباره» (round) صفحهٔ بعدی نتایج بینگ را می‌خواند.</para>
/// <para>کیفیت: دانلود دسته‌ای ۶تایی (۳ هم‌زمان) تا وقتی به تعداد خواسته برسد، هر فایل ≤ ۵ مگابایت، ضلع کوتاه ≥ ۶۰۰،
/// حذف تکراری‌ها با dHash (هم با تصویرهای فعلی کالا)، ذخیره با همان پاک‌سازی آپلود دستی (WebP، بدون متادیتا، نام تصادفی).</para>
/// </summary>
public sealed class SmartImageFinder(
    IHttpClientFactory httpFactory,
    IUploadStore uploads,
    SmartSources sources,
    ISmartConfigService config,
    ISmartCatalogService catalog,
    ILogger<SmartImageFinder> logger)
{
    public const int MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>کمترین شباهت عنوان صفحهٔ فروشگاه (نتایج جست‌وجو) برای برداشتن تصویرهایش.</summary>
    public const double ShopTitleMatch = 0.6;

    /// <summary>کمترین شباهت عنوان کارت تصویر بینگ.</summary>
    public const double BingTitleMatch = 0.5;

    /// <summary>
    /// نامزدهای تصویر یک کالا را از همهٔ منابع جمع، با فهرست سیاه فیلتر، رتبه‌بندی و تا <paramref name="want"/> عکس سالم ذخیره می‌کند.
    /// round = شمارهٔ دور «رد همه و جست‌وجوی دوباره» (۰ = بار اول).
    /// </summary>
    public async Task<List<FoundImage>> FindForProductAsync(SmartProductSubject subject, int want, int round, CancellationToken cancellationToken)
    {
        var options = await config.GetAsync(cancellationToken);
        var blacklist = await catalog.GetImageBlacklistAsync(subject.Id, cancellationToken);
        var scores = await catalog.ImageSourceScoresAsync(cancellationToken);
        var candidates = new List<ImageCandidate>();

        // دور ۲۳: مدل هر منبع خودکار بررسی می‌شود (عنوان + ردیف «مدل» مشخصات همان صفحه). منبعِ «مدل دیگر» کنار می‌رود.
        ModelVerdict Verify(string? text) => ImageModelCheck.Check(subject.Name, subject.EnglishName + " " + subject.Mpn, text);
        var hit = await sources.FindDigikalaAsync(subject, cancellationToken);
        var digikalaVerdict = hit is null ? ModelVerdict.Unknown : Verify(hit.Title + " " + hit.EnglishTitle);
        if (hit is not null && digikalaVerdict != ModelVerdict.Different && await sources.DigikalaDetailsAsync(hit.Id, cancellationToken) is { } details)
            candidates.AddRange(details.Images.Select(u => new ImageCandidate(u, "digikala", true, 1, hit.Title, null, digikalaVerdict)));
        if (!string.IsNullOrWhiteSpace(subject.SupplierUrl))
        {
            var (_, images) = await sources.PageAsync(subject.SupplierUrl, cancellationToken);
            candidates.AddRange(images.Take(6).Select(u => new ImageCandidate(u, "supplier", true, 1, null, subject.SupplierUrl)));
        }
        candidates.AddRange((await sources.BasalamImagesAsync(subject.SearchQuery, cancellationToken))
            .Select(u => new ImageCandidate(u, "basalam", true, 0.8)));

        // منابع عمومی همیشه جست‌وجو می‌شوند (کارشناس از بین ۱۰ تا ۱۵ نامزد انتخاب می‌کند)، ولی فقط با عنوان مطمئن.
        foreach (var shop in await sources.SerpShopHitsAsync(subject.SearchQuery, 4, ShopTitleMatch, cancellationToken))
        {
            var (specs, images) = await sources.PageAsync(shop.Url, cancellationToken);
            var model = string.Join(' ', specs.Where(s => ImageModelCheck.IsModelKey(s.Key)).Select(s => s.Value).Take(3));
            var verdict = Verify(shop.Title + " " + model);
            if (verdict == ModelVerdict.Different)
                continue;
            var key = SmartImageRules.SourceKey("serp", shop.Url);
            candidates.AddRange(images.Take(3).Select(u => new ImageCandidate(u, key, false, shop.Match, shop.Title, shop.Url, verdict)));
        }
        foreach (var image in await sources.BingImageResultsAsync(subject.SearchQuery, round, cancellationToken))
        {
            if (image.Title is null)
                continue;
            var match = Math.Max(SmartParsers.TitleMatch(subject.Name, image.Title),
                string.IsNullOrWhiteSpace(subject.EnglishName) ? 0 : SmartParsers.TitleMatch(subject.SearchQuery, image.Title));
            var bingVerdict = Verify(image.Title);
            if (match >= BingTitleMatch && bingVerdict != ModelVerdict.Different)
                candidates.Add(new ImageCandidate(image.Url, SmartImageRules.SourceKey("bing", image.PageUrl ?? image.Url), false, match, image.Title, image.PageUrl, bingVerdict));
        }

        var ranked = Rank(candidates, blacklist, scores, Math.Clamp(options.MaxImageCandidates, 6, 36));
        var existing = subject.ImageNames.Select(n => uploads.GetPath(UploadArea.Products, n)).Where(p => p is not null && File.Exists(p))
            .Select(p => SmartImageAnalyzer.Analyze(File.ReadAllBytes(p!))).Where(i => i is not null).Select(i => i!.Hash).ToList();
        return await DownloadAndStoreAsync(ranked, existing, blacklist, scores, Math.Clamp(want, 1, 15), options.MinImageSide, UploadArea.Products, cancellationToken);
    }

    /// <summary>
    /// مرتب‌سازی و پالایش نامزدها (خالص؛ آزمون‌پذیر): حذف آدرس‌های فهرست سیاه، حذف آدرس تکراری (اولی با امتیاز بیشتر می‌ماند)،
    /// مرتب‌سازی نزولی امتیاز و سقف تعداد. مثال: عکسی که کارشناس قبلاً رد کرده ← حذف؛ دیجی‌کالا جلوتر از بینگ ناشناس.
    /// </summary>
    public static List<ImageCandidate> Rank(IEnumerable<ImageCandidate> candidates, ImageBlacklist blacklist, IReadOnlyDictionary<string, double>? scores, int max) =>
        candidates
            .Where(c => SmartParsers.CleanImageUrl(c.Url) is not null && !blacklist.BlocksUrl(c.Url))
            .OrderByDescending(c => c.Score(scores))
            .ThenBy(c => c.Url, StringComparer.Ordinal)
            .DistinctBy(c => SmartImageRules.UrlKey(c.Url))
            .Take(Math.Max(1, max))
            .ToList();

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

    /// <summary>
    /// دور ۲۲: «۴ تصویر پیشنهادی دسته». از عکس کالاهای خود دسته (هر کالا یک عکس؛ کالای فعال و پرفروش جلوتر) تا <paramref name="want"/>
    /// بنر بدون متن ساخته می‌شود. عکس کالای تکراری/خیلی شبیه (dHash) کنار می‌رود تا گزینه‌ها واقعاً متفاوت باشند.
    /// <para>
    /// «پیشنهاد دوباره»: <paramref name="avoid"/> = تصویرهای پیشنهادی فعلی. چون ساخت بنر قطعی است (یک عکس ← همیشه همان بنر)،
    /// بنری که شبیه یکی از این‌ها باشد کنار می‌رود و فقط گزینهٔ تازه برمی‌گردد.
    /// </para>
    /// <para>هیچ دانلودی از اینترنت نیست (فقط عکس‌های محلی همین فروشگاه) ← سریع، امن و بدون مشکل حق نشر.</para>
    /// مثال: ۱۰ عکس کالا، want=4، avoid=[۴ بنر قبلی] ← ۴ بنر از عکس‌های پنجم به بعد.
    /// </summary>
    public async Task<List<string>> CategoryOptionsAsync(IReadOnlyList<string> productImageNames, int want, IReadOnlyList<string>? avoid,
        CancellationToken cancellationToken)
    {
        var results = new List<string>();
        if (productImageNames.Count == 0 || want <= 0)
            return results;
        var avoidHashes = new List<ulong>();
        foreach (var name in avoid ?? [])
        {
            var existing = uploads.GetPath(UploadArea.Categories, Path.GetFileName(name));
            if (existing is not null && File.Exists(existing) && SmartImageAnalyzer.Analyze(await File.ReadAllBytesAsync(existing, cancellationToken)) is { } info)
                avoidHashes.Add(info.Hash);
        }
        var sourceHashes = new List<ulong>();
        foreach (var name in productImageNames.Distinct(StringComparer.Ordinal).Take(12))
        {
            if (results.Count >= want)
                break;
            var path = uploads.GetPath(UploadArea.Products, Path.GetFileName(name));
            if (path is null || !File.Exists(path))
                continue;
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            if (bytes.Length > MaxImageBytes || SmartImageAnalyzer.Analyze(bytes) is not { } source || sourceHashes.Any(h => ImageHash.IsDuplicate(h, source.Hash)))
                continue;
            sourceHashes.Add(source.Hash);
            var banner = BannerGenerator.Create(bytes);
            if (banner is null)
                continue;
            if (avoidHashes.Count > 0 && SmartImageAnalyzer.Analyze(banner) is { } made && avoidHashes.Any(h => ImageHash.IsDuplicate(h, made.Hash)))
                continue;
            var stored = await StoreAsync(banner, UploadArea.Categories, cancellationToken);
            if (stored is not null)
                results.Add(stored);
        }
        return results;
    }

    /// <summary>حذف فایل‌های تصویر دسته (فقط نام فایل ساده داخل پوشهٔ دسته‌ها).</summary>
    public void DeleteCategoryFiles(IEnumerable<string> names) =>
        uploads.Delete(UploadArea.Categories, names.Select(n => Path.GetFileName(n)).Where(n => !string.IsNullOrWhiteSpace(n)).ToList());

    /// <summary>
    /// دور ۲۱ – پنل «جست‌وجوی عکس» استودیوی طراحی: نتایج تصویر بینگ (فقط آدرس‌های سالم http/https؛ round = صفحهٔ بعدی نتایج).
    /// چیزی دانلود نمی‌شود؛ مرورگر مدیر پیش‌نمایش را مستقیم نشان می‌دهد و فقط عکس انتخاب‌شده با <see cref="ImportAsync"/> وارد می‌شود.
    /// </summary>
    public async Task<List<ImageHit>> SearchWebAsync(string query, int round, CancellationToken cancellationToken)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2)
            return [];
        if (query.Length > 80)
            query = query[..80];
        var hits = await sources.BingImageResultsAsync(query, round, cancellationToken);
        return hits.Where(h => SmartParsers.CleanImageUrl(h.Url) is not null).Take(30).ToList();
    }

    /// <summary>
    /// دور ۲۱: وارد کردن یک عکس انتخاب‌شده از اینترنت به پوشهٔ محلی (همان دانلود امن یابنده: سقف ۵ مگابایت، مسدود بودن آدرس‌های
    /// داخلی شبکه، پاک‌سازی و WebP). عکس کوچک‌تر از <paramref name="minSide"/> پذیرفته نمی‌شود. خروجی null ← دانلود/تصویر نامعتبر.
    /// مثال: ImportAsync("https://…/a.jpg", UploadArea.Content, 300) ← FoundImage("9f3c….webp", 1200, 800, "web", false, …).
    /// </summary>
    public async Task<FoundImage?> ImportAsync(string url, UploadArea area, int minSide, CancellationToken cancellationToken)
    {
        if (SmartParsers.CleanImageUrl(url) is null)
            return null;
        var bytes = await DownloadAsync(url, cancellationToken);
        if (bytes is null)
            return null;
        var info = SmartImageAnalyzer.Analyze(bytes);
        if (info is null || Math.Min(info.Width, info.Height) < minSide)
            return null;
        var name = await StoreAsync(bytes, area, cancellationToken);
        return name is null ? null : new FoundImage(name, info.Width, info.Height, "web", false, url, unchecked((long)info.Hash));
    }

    /// <summary>حذف فایل‌های پیشنهادی ردشده (فقط نام فایل معتبر داخل پوشهٔ محصولات).</summary>
    /// <summary>
    /// دور ۲۳ (مورد ۲۷): نام انگلیسی و خوانا برای عکس‌های <b>تأییدشده</b> کالا (سئوی تصویر). فایل در همان پوشه جابه‌جا می‌شود.
    /// مثال: («9f3c….webp»، «samsung-galaxy-a55») ← «samsung-galaxy-a55-1a2b3c4d.webp». اگر نامک لاتین نباشد یا جابه‌جایی
    /// خطا بدهد، نام قبلی می‌ماند (هیچ عکسی گم نمی‌شود). خروجی هم‌ترتیب ورودی است.
    /// </summary>
    public List<string> SeoRename(IReadOnlyList<string> names, string? slug)
    {
        var result = new List<string>(names.Count);
        foreach (var name in names)
        {
            var source = uploads.GetPath(UploadArea.Products, name);
            var target = SeoImageName.Build(slug, Path.GetExtension(name));
            if (source is null || target is null)
            {
                result.Add(name);
                continue;
            }
            try
            {
                File.Move(source, Path.Combine(Path.GetDirectoryName(source)!, target), overwrite: false);
                result.Add(target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Image {File} could not be renamed", name);
                result.Add(name);
            }
        }
        return result;
    }

    public void DeleteProductFiles(IEnumerable<string> names) => uploads.Delete(UploadArea.Products, names.Select(n => Path.GetFileName(n)).Where(n => !string.IsNullOrWhiteSpace(n)).ToList());

    private async Task<List<FoundImage>> DownloadAndStoreAsync(List<ImageCandidate> candidates, List<ulong> existing, ImageBlacklist blacklist,
        IReadOnlyDictionary<string, double>? scores, int want, int minSide, UploadArea area, CancellationToken cancellationToken)
    {
        var results = new List<FoundImage>();
        var hashes = new List<ulong>(existing);
        using var throttle = new SemaphoreSlim(3);
        // دسته‌های ۶تایی به ترتیب امتیاز: وقتی به تعداد خواسته رسیدیم بقیه اصلاً دانلود نمی‌شوند (پهنای باند و حافظهٔ هاست ضعیف).
        foreach (var batch in candidates.Chunk(6))
        {
            if (results.Count >= want)
                break;
            var downloads = batch.Select(async c =>
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
                if (bytes is null || results.Count >= want)
                    continue;
                var info = SmartImageAnalyzer.Analyze(bytes);
                if (info is null || !SmartImageAnalyzer.IsAcceptable(info, minSide) || blacklist.BlocksHash(info.Hash)
                    || hashes.Any(h => ImageHash.IsDuplicate(h, info.Hash)))
                    continue;
                var name = await StoreAsync(bytes, area, cancellationToken);
                if (name is null)
                    continue;
                hashes.Add(info.Hash);
                results.Add(new FoundImage(name, info.Width, info.Height, candidate.Source, candidate.Trusted,
                    candidate.Url, unchecked((long)info.Hash), candidate.PageUrl, candidate.Title, candidate.Score(scores), candidate.Model, info.Quality));
            }
        }
        // دور ۲۴ (پیشنهاد ۵۸): عکس باکیفیت‌تر (تیز، روشن، زمینهٔ تمیز) جلوتر؛ اولین عکس پذیرفته‌شده عکس اصلی کالا می‌شود.
        return results.OrderByDescending(r => r.Score * 0.7 + r.Quality / 100.0 * 0.3).ToList();
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
