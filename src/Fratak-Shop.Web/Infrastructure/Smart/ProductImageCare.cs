using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Web.Infrastructure.Design;
using Fratak_Shop.Web.Infrastructure.Media;
using Fratak_Shop.Web.Services;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>
/// دور ۲۹ (#۵ و #۶) – «مراقبت ساعتی عکس کالاها»: همهٔ کالاها (منتشرشده، پیش‌نویس، تازه و واردشده) کم‌کم یکدست و تمیز می‌شوند.
/// <list type="number">
/// <item><b>پاک کردن لوگو/واترمارک</b> (تنظیم CleanWatermarks): هر عکس یک بار با <see cref="ImageCleanup.Plan"/> بررسی می‌شود؛
/// لوگوی گوشه یا نوشتهٔ کم‌رنگ روی زمینهٔ یکدست با رنگ زمینه پوشانده می‌شود و نسخهٔ تمیز جای عکس می‌نشیند.
/// عکسی که بعد از تمیزکاری هنوز «واترمارک‌دار» است و کالا عکس سالم دیگری دارد کنار گذاشته می‌شود (حذف نرم؛ از سطل بازیابی برمی‌گردد).</item>
/// <item><b>عکس اصلی بدون زمینه</b> (تنظیم PrimaryCutout): بهترین عکس با <see cref="ImageCleanup.PickPrimary"/> انتخاب می‌شود؛
/// بعد با «دوربری مطمئن» (<see cref="SubjectMatting"/>: مدل U²-Net + اعتبارسنجی <see cref="CutoutMask"/>) زمینه‌اش حذف و روی بوم
/// مربعِ شفاف با حاشیهٔ یکسان گذاشته می‌شود تا همهٔ کارت‌های سایت یک‌شکل دیده شوند.
/// <para><b>دور ۳۰ (#۴): عکس اصلی هرگز عوض/پاک نمی‌شود.</b> عکس بدون زمینه یک <b>عکس تازه</b> است که اول گالری و «اصلی» می‌شود و عکس
/// زمینه‌دار درست بعد از آن می‌ماند. مثال: کالا با ۲ عکس ← بعد از دوربری ۳ عکس (بدون زمینه، اصلیِ زمینه‌دار، عکس دوم).
/// دوربری‌ای که اعتبارسنجی را رد کند (خطر پاک شدن بخشی از کالا) اصلاً ساخته نمی‌شود.</para></item>
/// </list>
/// <para>سبک برای هاست ضعیف: هر ساعت حداکثر <see cref="CleanPerRun"/> عکس و <see cref="CutoutPerRun"/> کالا؛ پردازش فقط با مجوز
/// <see cref="WebPEncoder.Gate"/> (هم‌زمان با تبدیل‌های دیگر سنگین نمی‌شود). نتیجهٔ هر عکس با بیت‌های SubjectFlags علامت می‌خورد
/// (<see cref="ImageCleanup.CareDone"/>، <see cref="ImageCleanup.CutoutDone"/>، <see cref="CutoutTried"/>) تا دوباره کار نشود.</para>
/// <para>تمیزکاری لوگو: فایل قبلی روی دیسک می‌ماند (ممکن است در بنری استفاده شده باشد)؛ فقط ردیف عکس به فایل تازه اشاره می‌کند.</para>
/// <para>اجرا: SmartWorker (همان حلقهٔ ساعتی اثر انگشت عکس‌ها). خطای یک عکس فقط ثبت می‌شود و بقیه ادامه می‌یابند.</para>
/// </summary>
public static class ProductImageCare
{
    /// <summary>بیشترین عکس برای «تمیزکاری لوگو» در هر اجرا.</summary>
    public const int CleanPerRun = 40;

    /// <summary>بیشترین کالا برای «عکس اصلی دوربری‌شده» در هر اجرا.</summary>
    public const int CutoutPerRun = 20;

    /// <summary>
    /// بیت «دوربری این عکس امتحان شد» تا هر ساعت تکرار نشود: یا ممکن نبود (اعتبارسنجی رد کرد)، یا (دور ۳۰) نسخهٔ بدون زمینه‌اش
    /// به‌صورت عکس جدا ساخته شده و این همان عکس اصلیِ زمینه‌دار است که نگه داشته شده.
    /// </summary>
    public const byte CutoutTried = 128;

    /// <summary>ضلع نسخهٔ کوچکی که بررسی لوگو روی آن انجام می‌شود.</summary>
    private const int AnalysisSide = 320;

    /// <summary>عکس‌های بزرگ‌تر از این (پیکسل) پردازش نمی‌شوند تا حافظهٔ هاست پر نشود.</summary>
    private const long MaxPixels = 25_000_000;

    /// <summary>خلاصهٔ یک اجرا.</summary>
    public sealed record CareReport(int Checked, int Cleaned, int Removed, int Cutouts, int PrimarySwitched);

    /// <summary>یک اجرای کامل (هر دو بخش، طبق تنظیمات). خاموش بودن «محتوای هوشمند» ← هیچ کاری.</summary>
    public static async Task<CareReport> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var options = await services.GetRequiredService<ISmartConfigService>().GetAsync(cancellationToken);
        if (!options.Enabled)
            return new CareReport(0, 0, 0, 0, 0);
        var context = services.GetRequiredService<FratakShopDbContext>();
        var uploads = services.GetRequiredService<IUploadStore>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Fratak.ImageCare");
        var (checkedCount, cleaned, removed) = options.CleanWatermarks
            ? await CleanAsync(context, uploads, logger, cancellationToken)
            : (0, 0, 0);
        var (cutouts, switched) = options.PrimaryCutout
            ? await PrimaryAsync(context, uploads, logger, cancellationToken)
            : (0, 0);
        context.ChangeTracker.Clear();
        return new CareReport(checkedCount, cleaned, removed, cutouts, switched);
    }

    // ───────────────────────────── ۱) لوگو و واترمارک ─────────────────────────────

    private static async Task<(int Checked, int Cleaned, int Removed)> CleanAsync(FratakShopDbContext context, IUploadStore uploads, ILogger logger,
        CancellationToken cancellationToken)
    {
        // فقط عکس‌هایی که «بررسی سوژه» شده‌اند (SubjectScore پر است) و هنوز تمیزکاری نشده‌اند؛ کالای منتشرشده و عکس تازه اول.
        var rows = await context.ProductImages.AsTracking()
            .Where(i => i.SubjectScore != null && (i.SubjectFlags == null || (i.SubjectFlags & ImageCleanup.CareDone) == 0))
            .OrderByDescending(i => i.Product.IsActive)
            .ThenByDescending(i => i.CreatedAt)
            .Take(CleanPerRun)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return (0, 0, 0);

        var cleaned = 0;
        var stillMarked = new List<Guid>();
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte flags = row.SubjectFlags ?? (byte)0;
            var path = uploads.GetPath(UploadArea.Products, Path.GetFileName(row.ImageName));
            if (path is null || !File.Exists(path))
            {
                row.SubjectFlags = (byte)(flags | ImageCleanup.CareDone);
                continue;
            }
            CleanResult? result;
            try
            {
                result = CleanFile(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Cleaning image {Image} failed", row.ImageName);
                row.SubjectFlags = (byte)(flags | ImageCleanup.CareDone);
                continue;
            }
            if (result is null)
                continue; // سرور شلوغ بود (مجوز پردازش نرسید) ← ساعت بعد دوباره
            if (result.Bytes is { } bytes)
            {
                var stored = await SaveAsync(uploads, row.ImageName, bytes, "clean", cancellationToken);
                if (stored is not null)
                {
                    var info = SmartImageAnalyzer.Analyze(bytes);
                    var subject = info is null ? null : SmartImageAnalyzer.Subject(bytes, quality: info.Quality);
                    row.ImageName = stored;
                    if (info is not null)
                    {
                        row.Hash = unchecked((long)info.Hash);
                        row.Quality = (byte)Math.Clamp(info.Quality, 0, 100);
                    }
                    if (subject is not null)
                    {
                        row.SubjectScore = ImageSubjectCheck.Score(subject);
                        flags = ImageSubjectCheck.Flags(subject);
                    }
                    // نسخهٔ تمیز دوباره باید دوربری شود (اگر عکس اصلی است).
                    flags = (byte)(flags & ~(ImageCleanup.CutoutDone | CutoutTried));
                    cleaned++;
                }
            }
            row.SubjectFlags = (byte)(flags | ImageCleanup.CareDone);
            if ((flags & 8) != 0)
                stillMarked.Add(row.Id);
        }
        await context.SaveChangesAsync(cancellationToken);

        // «هرگز عکس واترمارک‌دار نشان داده نشود»: اگر کالا عکس سالم دیگری دارد، عکس واترمارک‌دار کنار می‌رود (حذف نرم).
        var removed = 0;
        if (stillMarked.Count > 0)
        {
            var productIds = rows.Where(r => stillMarked.Contains(r.Id)).Select(r => r.ProductId).Distinct().ToList();
            var gallery = await context.ProductImages.AsTracking().Where(i => productIds.Contains(i.ProductId)).ToListAsync(cancellationToken);
            foreach (var group in gallery.GroupBy(i => i.ProductId))
            {
                var drop = WatermarkedToDrop(group.Select(i => (i.Id, i.SubjectFlags)).ToList(), stillMarked);
                if (drop.Count == 0)
                    continue;
                var dropped = group.Where(i => drop.Contains(i.Id)).ToList();
                var lostPrimary = dropped.Any(i => i.IsPrimary);
                context.ProductImages.RemoveRange(dropped);
                removed += dropped.Count;
                if (lostPrimary)
                {
                    var rest = group.Except(dropped).ToList();
                    var pick = ImageCleanup.PickPrimary(rest.Select(Candidate).ToList());
                    foreach (var image in rest)
                        image.IsPrimary = image.Id == pick;
                }
            }
            if (removed > 0)
                await context.SaveChangesAsync(cancellationToken);
        }
        if (cleaned > 0 || removed > 0)
            logger.LogInformation("Image care: {Cleaned} images cleaned, {Removed} watermarked images removed", cleaned, removed);
        return (rows.Count, cleaned, removed);
    }

    /// <summary>
    /// عکس‌های واترمارک‌داری که باید کنار بروند (خالص؛ آزمون‌پذیر): فقط اگر کالا دست‌کم یک عکس بدون واترمارک داشته باشد
    /// (کالا هرگز بی‌عکس نمی‌شود؛ آن عکس در گزارش سلامت داده می‌ماند و جست‌وجوی روزانهٔ عکس جایگزین پیدا می‌کند).
    /// مثال: [سالم، واترمارک‌دار] ← [واترمارک‌دار]؛ [واترمارک‌دار] ← [].
    /// </summary>
    public static List<Guid> WatermarkedToDrop(IReadOnlyList<(Guid Id, byte? Flags)> gallery, IReadOnlyCollection<Guid> marked)
    {
        var clean = gallery.Count(i => ((i.Flags ?? 0) & 8) == 0);
        if (clean == 0)
            return [];
        return gallery.Where(i => marked.Contains(i.Id) && ((i.Flags ?? 0) & 8) != 0).Select(i => i.Id).ToList();
    }

    /// <summary>نتیجهٔ تمیزکاری یک فایل: Bytes = نسخهٔ تمیز (WebP) یا null یعنی «تغییری لازم نبود».</summary>
    private sealed record CleanResult(byte[]? Bytes, string Reason);

    /// <summary>بررسی و تمیزکاری یک فایل (با مجوز پردازش). مجوز نرسید ← null.</summary>
    private static CleanResult? CleanFile(string path)
    {
        if (!WebPEncoder.Gate.Wait(TimeSpan.FromSeconds(30)))
            return null;
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec is null || codec.Info.Width < 64 || codec.Info.Height < 64 || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
                return new CleanResult(null, "عکس خوانده نشد یا خیلی بزرگ است");
            using var decoded = SKBitmap.Decode(codec);
            if (decoded is null)
                return new CleanResult(null, "عکس خوانده نشد");
            // عکس از قبل شفاف (دوربری‌شده) لوگوی زمینه ندارد.
            if (ImageColors.HasTransparentBorder(decoded))
                return new CleanResult(null, "عکس شفاف");
            using var oriented = MediaEndpoints.Orient(decoded, codec.EncodedOrigin);
            var bitmap = oriented ?? decoded;
            var (rgb, w, h) = SmallRgb(bitmap, AnalysisSide);
            // دور ۳۰ (#۱۰): ماسک سوژه از مدل دوربری ← لوگو/نوشتهٔ «هر جای زمینه» پیدا می‌شود؛ نوشتهٔ روی خود کالا دست نمی‌خورد.
            var subject = SubjectMatting.SubjectMask(rgb, w, h);
            var plan = ImageCleanup.Plan(rgb, w, h, subject?.Mask, subject?.Soft);
            if (plan.Action != CleanupAction.Clean)
                return new CleanResult(null, plan.Reason);

            // کادرها روی اندازهٔ اصلی (با ۲ پیکسل حاشیهٔ اطمینان) با رنگ زمینه پوشانده می‌شوند.
            using var full = bitmap.Copy(SKColorType.Rgba8888);
            if (full is null)
                return new CleanResult(null, "کپی عکس ممکن نشد");
            var sx = full.Width / (double)w;
            var sy = full.Height / (double)h;
            var (r, g, b) = plan.Background;
            var fill = new SKColor(r, g, b);
            using (var canvas = new SKCanvas(full))
            using (var paint = new SKPaint { Color = fill, IsAntialias = false })
            {
                foreach (var box in plan.Boxes)
                {
                    var rect = SKRect.Create((float)(box.X * sx) - 2, (float)(box.Y * sy) - 2, (float)(box.Width * sx) + 4, (float)(box.Height * sy) + 4);
                    canvas.DrawRect(rect, paint);
                }
                canvas.Flush();
            }
            if (plan.FillGhost)
            {
                // نوشتهٔ کم‌رنگ روی زمینه: پیکسل‌های نزدیک به رنگ زمینه یکدست می‌شوند (همان آستانهٔ ImageCleanup.Apply).
                // مستقیم روی بایت‌های RGBA (چند برابر سریع‌تر از GetPixel/SetPixel روی عکس بزرگ).
                var pixels = full.GetPixelSpan();
                for (var i = 0; i + 3 < pixels.Length; i += 4)
                {
                    if (ImageSubjectCheck.Distance((pixels[i], pixels[i + 1], pixels[i + 2]), plan.Background) > 38)
                        continue;
                    pixels[i] = r;
                    pixels[i + 1] = g;
                    pixels[i + 2] = b;
                    pixels[i + 3] = 255;
                }
            }
            using var image = SKImage.FromBitmap(full);
            using var data = image.Encode(SKEncodedImageFormat.Webp, WebPEncoder.Quality);
            return new CleanResult(data?.ToArray(), plan.Reason);
        }
        finally
        {
            WebPEncoder.Gate.Release();
        }
    }

    /// <summary>نسخهٔ کوچک RGB (پیکسل شفاف = سفید) برای موتور تمیزکاری.</summary>
    private static (byte[] Rgb, int Width, int Height) SmallRgb(SKBitmap bitmap, int side)
    {
        var scale = Math.Min(1.0, side / (double)Math.Max(bitmap.Width, bitmap.Height));
        var w = Math.Max(16, (int)Math.Round(bitmap.Width * scale));
        var h = Math.Max(16, (int)Math.Round(bitmap.Height * scale));
        using var small = bitmap.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul), new SKSamplingOptions(SKFilterMode.Linear))
                          ?? throw new InvalidOperationException("resize failed");
        var rgb = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = small.GetPixel(x, y);
                var i = (y * w + x) * 3;
                var a = c.Alpha / 255.0;
                rgb[i] = (byte)Math.Round(c.Red * a + 255 * (1 - a));
                rgb[i + 1] = (byte)Math.Round(c.Green * a + 255 * (1 - a));
                rgb[i + 2] = (byte)Math.Round(c.Blue * a + 255 * (1 - a));
            }
        }
        return (rgb, w, h);
    }

    // ───────────────────────────── ۲) عکس اصلی یکدست ─────────────────────────────

    private static async Task<(int Cutouts, int Switched)> PrimaryAsync(FratakShopDbContext context, IUploadStore uploads, ILogger logger,
        CancellationToken cancellationToken)
    {
        const byte doneOrTried = ImageCleanup.CutoutDone | CutoutTried;
        // کالاهایی که عکس اصلی‌شان هنوز دوربری نشده (یا اصلاً عکس اصلی ندارند)؛ عکس‌ها باید «بررسی سوژه» شده باشند.
        var productIds = await context.Products
            .Where(p => p.Images.Any() && p.Images.All(i => i.SubjectScore != null)
                        && !p.Images.Any(i => i.IsPrimary && i.SubjectFlags != null && (i.SubjectFlags & doneOrTried) != 0))
            .OrderByDescending(p => p.IsActive)
            .ThenByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.Slug })
            .Take(CutoutPerRun)
            .ToListAsync(cancellationToken);
        if (productIds.Count == 0)
            return (0, 0);

        var ids = productIds.Select(p => p.Id).ToList();
        var slugs = productIds.ToDictionary(p => p.Id, p => p.Slug);
        var gallery = await context.ProductImages.AsTracking().Where(i => ids.Contains(i.ProductId)).ToListAsync(cancellationToken);
        int cutouts = 0, switched = 0;
        foreach (var group in gallery.GroupBy(i => i.ProductId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var images = group.ToList();
            var pick = ImageCleanup.PickPrimary(images.Select(Candidate).ToList());
            if (pick is null)
                continue;
            if (images.Any(i => i.IsPrimary != (i.Id == pick)))
            {
                foreach (var image in images)
                    image.IsPrimary = image.Id == pick;
                switched++;
            }
            var primary = images.First(i => i.Id == pick);
            byte flags = primary.SubjectFlags ?? (byte)0;
            if ((flags & doneOrTried) != 0)
                continue;
            if ((flags & (4 | 8)) != 0)
            {
                // آدم/واترمارک: دوربری مطمئن ممکن نیست (مدل آدم را هم سوژه می‌بیند)؛ جایگزینش را جست‌وجوی روزانهٔ عکس پیدا می‌کند.
                // دور ۳۰: عکس «صحنه» (کالا روی میز/در محیط) دیگر رد نمی‌شود؛ مدل U²-Net کالا را جدا و CutoutMask اعتبارسنجی می‌کند.
                primary.SubjectFlags = (byte)(flags | CutoutTried);
                continue;
            }
            var path = uploads.GetPath(UploadArea.Products, Path.GetFileName(primary.ImageName));
            if (path is null || !File.Exists(path))
            {
                primary.SubjectFlags = (byte)(flags | CutoutTried);
                continue;
            }
            CutoutOutcome outcome;
            try
            {
                outcome = MakeCutout(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Cutout of {Image} failed", primary.ImageName);
                primary.SubjectFlags = (byte)(flags | CutoutTried);
                continue;
            }
            switch (outcome.Kind)
            {
                case CutoutKind.Busy:
                    continue; // ساعت بعد
                case CutoutKind.AlreadyTransparent:
                    primary.SubjectFlags = (byte)((flags & ~ImageCleanup.ProblemBits) | ImageCleanup.CutoutDone);
                    cutouts++;
                    continue;
                case CutoutKind.NotPossible:
                    primary.SubjectFlags = (byte)(flags | CutoutTried);
                    continue;
            }
            var stored = await SaveAsync(uploads, primary.ImageName, outcome.Png!, "cut", cancellationToken, slugs.GetValueOrDefault(primary.ProductId));
            if (stored is null)
            {
                primary.SubjectFlags = (byte)(flags | CutoutTried);
                continue;
            }
            // دور ۳۰ (#۴): عکس بدون زمینه یک ردیف تازه است؛ عکس زمینه‌دار دست‌نخورده می‌ماند (فقط دیگر «اصلی» نیست).
            context.ProductImages.Add(CutoutRow(primary, images, stored, SmartImageAnalyzer.Analyze(outcome.Png!)?.Hash));
            primary.IsPrimary = false;
            primary.SubjectFlags = (byte)(flags | CutoutTried);
            cutouts++;
        }
        await context.SaveChangesAsync(cancellationToken);
        if (cutouts > 0 || switched > 0)
            logger.LogInformation("Image care: {Cutouts} primary cutouts, {Switched} primary images switched", cutouts, switched);
        return (cutouts, switched);
    }

    private enum CutoutKind
    {
        Busy,
        AlreadyTransparent,
        NotPossible,
        Created,
    }

    private sealed record CutoutOutcome(CutoutKind Kind, byte[]? Png = null);

    /// <summary>
    /// دور ۳۰ (#۴) – ردیف عکس «بدون زمینه» کنار عکس اصلی (خالص؛ آزمون‌پذیر). عکس تازه اصلی و اول گالری می‌شود و بقیه یک خانه عقب
    /// می‌روند؛ متن جایگزین، کیفیت و امتیاز سوژه از عکس اصلی گرفته می‌شود (فقط زمینه برداشته شده) و نشانه‌ها: سفید + یکدست +
    /// تمیزشده + دوربری‌شده. مثال: گالری [A (۰، اصلی)، B (۱)] ← [برش (۰، اصلی)، A (۱)، B (۲)].
    /// </summary>
    /// <param name="original">عکس اصلیِ زمینه‌دار (ردیف آن این‌جا تغییر نمی‌کند؛ صدا زننده IsPrimary را خاموش می‌کند).</param>
    /// <param name="gallery">همهٔ عکس‌های همان کالا (ترتیب نمایش‌شان یک خانه جلو می‌رود).</param>
    /// <param name="imageName">نام فایل ذخیره‌شدهٔ عکس بدون زمینه.</param>
    /// <param name="hash">اثر انگشت dHash عکس تازه (یا null).</param>
    public static Fratak_Shop.Data.Entities.Products.ProductImage CutoutRow(Fratak_Shop.Data.Entities.Products.ProductImage original,
        IReadOnlyList<Fratak_Shop.Data.Entities.Products.ProductImage> gallery, string imageName, ulong? hash)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(gallery);
        var first = gallery.Count == 0 ? 0 : Math.Max(0, gallery.Min(i => i.DisplayOrder));
        foreach (var image in gallery)
        {
            if (image.DisplayOrder >= first)
                image.DisplayOrder++;
        }
        var flags = original.SubjectFlags ?? (byte)0;
        return new Fratak_Shop.Data.Entities.Products.ProductImage
        {
            Id = Guid.NewGuid(),
            ProductId = original.ProductId,
            ImageName = imageName,
            AltText = original.AltText,
            DisplayOrder = first,
            IsPrimary = true,
            Hash = hash is { } h && h != 0 ? unchecked((long)h) : null,
            Quality = original.Quality,
            SubjectScore = original.SubjectScore,
            SubjectFlags = (byte)((flags & ~ImageCleanup.ProblemBits & ~CutoutTried) | 1 | 2 | ImageCleanup.CareDone | ImageCleanup.CutoutDone),
        };
    }

    /// <summary>
    /// دوربری مطمئن و قاب یکسان: زمینه با <see cref="SubjectMatting"/> حذف (رد اعتبارسنجی ← NotPossible و عکس اصلی دست‌نخورده)،
    /// کالا وسط بوم مربعِ شفاف با ۶٪ حاشیه از هر طرف (ضلع ۸۰۰ تا ۱۴۰۰ پیکسل).
    /// مثال: هدفون ۷۲۰×۸۷۰ ← بوم ۹۸۹×۹۸۹ با هدفون وسط.
    /// </summary>
    private static CutoutOutcome MakeCutout(string path)
    {
        if (!WebPEncoder.Gate.Wait(TimeSpan.FromSeconds(30)))
            return new CutoutOutcome(CutoutKind.Busy);
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec is null || codec.Info.Width < 64 || codec.Info.Height < 64 || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
                return new CutoutOutcome(CutoutKind.NotPossible);
            using var decoded = SKBitmap.Decode(codec);
            if (decoded is null)
                return new CutoutOutcome(CutoutKind.NotPossible);
            if (ImageColors.HasTransparentBorder(decoded))
                return new CutoutOutcome(CutoutKind.AlreadyTransparent);
            using var oriented = MediaEndpoints.Orient(decoded, codec.EncodedOrigin);
            var cut = SubjectMatting.Cutout(oriented ?? decoded, SubjectMatting.DefaultSide);
            if (cut is null)
                return new CutoutOutcome(CutoutKind.NotPossible);
            using var piece = SKBitmap.Decode(cut.Png);
            if (piece is null)
                return new CutoutOutcome(CutoutKind.NotPossible);
            var side = UniformSide(piece.Width, piece.Height);
            using var surface = SKSurface.Create(new SKImageInfo(side, side, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (surface is null)
                return new CutoutOutcome(CutoutKind.NotPossible);
            surface.Canvas.Clear(SKColors.Transparent);
            var inner = side * 0.88f;
            var fit = Math.Min(inner / piece.Width, inner / piece.Height);
            var dw = piece.Width * fit;
            var dh = piece.Height * fit;
            using var image = SKImage.FromBitmap(piece);
            surface.Canvas.DrawImage(image, SKRect.Create((side - dw) / 2, (side - dh) / 2, dw, dh), new SKSamplingOptions(SKCubicResampler.Mitchell));
            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
            return data is null ? new CutoutOutcome(CutoutKind.NotPossible) : new CutoutOutcome(CutoutKind.Created, data.ToArray());
        }
        finally
        {
            WebPEncoder.Gate.Release();
        }
    }

    /// <summary>
    /// ضلع بوم مربع یکسان (خالص؛ آزمون‌پذیر): بزرگ‌ترین ضلع کالا ÷ ۰٫۸۸، بین ۸۰۰ و ۱۴۰۰.
    /// مثال: (۷۲۰، ۸۷۰) ← ۹۸۹؛ (۲۰۰، ۱۰۰) ← ۸۰۰؛ (۲۰۰۰، ۵۰۰) ← ۱۴۰۰.
    /// </summary>
    public static int UniformSide(int width, int height) =>
        Math.Clamp((int)Math.Ceiling(Math.Max(width, height) / 0.88), 800, 1400);

    // ───────────────────────────── کمکی ─────────────────────────────

    private static PrimaryCandidate Candidate(Fratak_Shop.Data.Entities.Products.ProductImage i) =>
        new(i.Id, i.IsPrimary, i.SubjectFlags, i.SubjectScore, i.Quality, i.DisplayOrder);

    /// <summary>
    /// ذخیرهٔ نسخهٔ تازه با نام خوانا: «{نامک کالا}-{رمز}» یا «{نام قبلی}-{پسوند}-{رمز}». نام نامعتبر ← نام تصادفی. خطا ← null.
    /// </summary>
    private static async Task<string?> SaveAsync(IUploadStore uploads, string oldName, byte[] bytes, string suffix, CancellationToken cancellationToken,
        string? slug = null)
    {
        var stem = slug is not null && SeoImageName.Build(slug, ".webp") is { } seo
            ? Path.GetFileNameWithoutExtension(seo)
            : Stem(oldName, suffix);
        var stored = stem is null ? null : await uploads.SaveImageAsAsync(UploadArea.Products, stem, bytes, cancellationToken);
        stored ??= await uploads.SaveImageAsync(UploadArea.Products, bytes, cancellationToken);
        return stored?.FileName;
    }

    /// <summary>
    /// نام تازه از نام قبلی (خالص؛ آزمون‌پذیر): پسوندهای قبلی «-clean-…»/«-cut-…» حذف و «-{suffix}-{رمز ۶ حرفی}» اضافه می‌شود.
    /// فقط حروف کوچک لاتین، رقم و خط تیره؛ نام فارسی/نامعتبر ← null (نام تصادفی ساخته می‌شود).
    /// مثال: («kettle-1a2b3c4d.webp»، «clean»، «9f3c2a») ← «kettle-1a2b3c4d-clean-9f3c2a».
    /// </summary>
    public static string? Stem(string oldName, string suffix, string? random = null)
    {
        var stem = Path.GetFileNameWithoutExtension(oldName ?? "").ToLowerInvariant();
        foreach (var mark in new[] { "-clean-", "-cut-" })
        {
            var at = stem.IndexOf(mark, StringComparison.Ordinal);
            if (at > 0)
                stem = stem[..at];
        }
        if (stem.Length == 0 || stem[0] == '-' || !stem.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            return null;
        if (stem.Length > 70)
            stem = stem[..70].TrimEnd('-');
        random ??= Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        return $"{stem}-{suffix}-{random}";
    }
}
