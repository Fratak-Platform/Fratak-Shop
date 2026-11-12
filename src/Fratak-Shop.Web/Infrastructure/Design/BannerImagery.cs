using System.Collections.Concurrent;
using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Web.Services;
using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Design;

/// <summary>
/// دور ۲۷ – «عکس‌های بنر»: هر بنری که ساخته می‌شود عکس واقعی می‌گیرد، نه فقط رنگ و متن.
/// <list type="bullet">
/// <item><b>عکس کالا ← دوربری‌شده</b> (<see cref="ProductAsync"/>): اگر زمینهٔ عکس سفید باشد، زمینه شفاف و حاشیه بریده می‌شود
/// (دور ۳۰: دوربری مطمئن <see cref="Fratak_Shop.Web.Infrastructure.Smart.SubjectMatting"/> با مدل U²-Net و اعتبارسنجی؛ هرگز بخشی از کالا پاک نمی‌شود) و یک بار در پوشهٔ /uploads/cutouts با نام قطعی ذخیره می‌شود؛ دفعه‌های بعد همان فایل استفاده می‌شود.
/// عکس زمینه‌دار (عکس محیطی) همان‌طور برمی‌گردد و قالب آن را داخل قاب می‌گذارد.</item>
/// <item><b>عکس فضاسازی دسته</b> (<see cref="CategoryPhoto"/>): عکس واقعی «-photo-» دسته (از جست‌وجوی وب دور ۲۶) یا عکسی که مدیر خودش
/// برای دسته گذاشته (افقی، پهنای ≥ ۹۰۰ و نه زمینهٔ سفید).</item>
/// <item><b>ابزار «حذف زمینهٔ سفید»</b> ویرایشگر (<see cref="RemoveBackgroundAsync"/>) روی هر عکس محلی.</item>
/// <item><b>پاک‌سازی</b> (<see cref="PruneCutouts"/>): برش‌هایی که عکس اصلی‌شان حذف شده پاک می‌شوند (فضای هاست پر نشود).</item>
/// </list>
/// <para>امنیت: فقط فایل‌های خود سایت در پوشه‌های شناخته‌شدهٔ /uploads خوانده می‌شوند (هیچ دانلودی از اینترنت نیست)؛ نام فایل فقط نام ساده
/// (بدون مسیر) پذیرفته می‌شود؛ سقف حجم ۱۵ مگابایت و ۴۰ مگاپیکسل.</para>
/// <para>محل استفاده: DesignStudio (ایده‌های خودکار، «ایده ← طرح»، قالب ویرایشگر) و Handler «Cutout» ویرایشگر.</para>
/// </summary>
public sealed class BannerImagery(IUploadStore uploads, ILogger logger)
{
    /// <summary>بیشترین ضلع عکس دوربری‌شده (برای بنر ۱۹۲۰ پیکسلی کافی است و حجم کم می‌ماند).</summary>
    public const int CutoutSide = 900;

    /// <summary>پوشه‌هایی که عکسشان در بنر مجاز است (به همان نامی که در آدرس /uploads/{نام}/ می‌آید).</summary>
    private static readonly IReadOnlyDictionary<string, UploadArea> Areas = new Dictionary<string, UploadArea>(StringComparer.Ordinal)
    {
        [UploadArea.Products.Name] = UploadArea.Products,
        [UploadArea.Categories.Name] = UploadArea.Categories,
        [UploadArea.Content.Name] = UploadArea.Content,
        [UploadArea.Banners.Name] = UploadArea.Banners,
        [UploadArea.Blogs.Name] = UploadArea.Blogs,
        [UploadArea.Brands.Name] = UploadArea.Brands,
        [UploadArea.Media.Name] = UploadArea.Media,
        [UploadArea.Cutouts.Name] = UploadArea.Cutouts,
    };

    /// <summary>
    /// عکس‌هایی که دوربری نمی‌شوند (زمینهٔ غیرسفید) تا دوباره رمزگشایی نشوند. کلید = نام فایل برش؛ سقف ۵۰۰۰ مورد (بعد خالی می‌شود).
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> NotCuttable = new(StringComparer.Ordinal);

    /// <summary>
    /// آدرس محلی ← (پوشه، نام فایل). فقط «/uploads/{پوشهٔ مجاز}/{نام ساده}». مثال: «/uploads/products/a1.webp» ← (Products، «a1.webp»)؛
    /// «/uploads/../x» یا «https://…» ← null.
    /// </summary>
    public static (UploadArea Area, string FileName)? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 300 || url.Contains("..", StringComparison.Ordinal) || url.Contains('\\'))
            return null;
        var path = url.Split('?', '#')[0];
        var parts = path.Split('/', StringSplitOptions.None);
        // ["", "uploads", area, file]
        if (parts.Length != 4 || parts[0].Length != 0 || parts[1] != "uploads" || !Areas.TryGetValue(parts[2], out var area))
            return null;
        var name = parts[3];
        if (name.Length is 0 or > 100 || Path.GetFileName(name) != name || name.StartsWith('.'))
            return null;
        return (area, name);
    }

    /// <summary>
    /// نام قطعی فایل برش. مثال: (Products، «9f3c.webp») ← «products-9f3c». نام با حروف بزرگ/«_»/غیرلاتین ← null.
    /// </summary>
    public static string? CutoutStem(UploadArea area, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        // خط زیر «_» پشتیبانی نمی‌شود تا از نام برش بشود نام عکس اصلی را بی‌ابهام برگرداند (پاک‌سازی)
        if (stem.Length is 0 or > 70 || !stem.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            return null;
        return area.Name + "-" + stem;
    }

    /// <summary>
    /// عکس محلی با ابعاد واقعی؛ Cutout = عکسِ از قبل شفاف (PNG/WebP بدون زمینه). فایل نبود/خراب/خیلی بزرگ ← null.
    /// </summary>
    public DesignImage? Picture(string? url)
    {
        if (Parse(url) is not { } parsed || uploads.GetPath(parsed.Area, parsed.FileName) is not { } path)
            return null;
        try
        {
            if (new FileInfo(path).Length > 15 * 1024 * 1024)
                return null;
            using var codec = SKCodec.Create(path);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 40_000_000)
                return null;
            var local = $"/uploads/{parsed.Area.Name}/{parsed.FileName}";
            if (parsed.Area == UploadArea.Cutouts)
                return new DesignImage(local, codec.Info.Width, codec.Info.Height) { Cutout = true };
            var cutout = false;
            if (codec.Info.AlphaType != SKAlphaType.Opaque)
            {
                using var bitmap = SKBitmap.Decode(codec);
                cutout = bitmap is not null && ImageColors.HasTransparentBorder(bitmap);
            }
            return new DesignImage(local, codec.Info.Width, codec.Info.Height) { Cutout = cutout };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            logger.LogDebug(exception, "Banner picture {Url} could not be read", url);
            return null;
        }
    }

    /// <summary>
    /// عکس کالا برای بنر: دوربری‌شده اگر ممکن باشد (از حافظهٔ /uploads/cutouts یا همین حالا ساخته می‌شود)، وگرنه خود عکس.
    /// مثال: «/uploads/products/a1.webp» (زمینهٔ سفید) ← «/uploads/cutouts/products-a1.webp» با Cutout=true.
    /// </summary>
    public async Task<DesignImage?> ProductAsync(string? url, CancellationToken cancellationToken)
    {
        if (Parse(url) is not { } parsed)
            return null;
        if (parsed.Area == UploadArea.Cutouts)
            return Picture(url);
        var stem = CutoutStem(parsed.Area, parsed.FileName);
        if (stem is not null)
        {
            // برش آماده (هر پسوندی که رمزگذار داده باشد)
            foreach (var extension in new[] { ".webp", ".png", ".jpg" })
            {
                if (uploads.GetPath(UploadArea.Cutouts, stem + extension) is not null)
                    return Picture($"/uploads/{UploadArea.Cutouts.Name}/{stem}{extension}") ?? Picture(url);
            }
            if (NotCuttable.ContainsKey(stem))
                return Picture(url);
        }
        var original = Picture(url);
        if (original is null || original.Cutout || stem is null)
            return original;
        var made = await MakeCutoutAsync(parsed.Area, parsed.FileName, stem, requireWhite: true, cancellationToken);
        if (made is null)
        {
            if (NotCuttable.Count > 5000)
                NotCuttable.Clear();
            NotCuttable.TryAdd(stem, 0);
        }
        return made ?? original;
    }

    /// <summary>
    /// ابزار ویرایشگر «حذف زمینهٔ سفید» روی هر عکس محلی. خروجی: عکس تازهٔ شفاف یا پیام خطای فارسی.
    /// برخلاف برش خودکار، این‌جا لازم نیست ۸۵٪ لبه‌ها سفید باشد؛ فقط باید چیزی (≥ ۳٪) برای برش وجود داشته باشد.
    /// </summary>
    public async Task<(DesignImage? Image, string? Error)> RemoveBackgroundAsync(string? url, CancellationToken cancellationToken)
    {
        if (Parse(url) is not { } parsed)
            return (null, "فقط عکس‌های ذخیره‌شده در خود سایت قابل دوربری‌اند.");
        var picture = Picture(url);
        if (picture is null)
            return (null, "عکس پیدا نشد یا خراب است.");
        if (picture.Cutout)
            return (picture, null);
        var stem = CutoutStem(parsed.Area, parsed.FileName);
        if (stem is null)
            return (null, "نام این عکس پشتیبانی نمی‌شود؛ عکس را دوباره آپلود کنید.");
        var made = await MakeCutoutAsync(parsed.Area, parsed.FileName, stem, requireWhite: false, cancellationToken);
        return made is null
            ? (null, "دوربری مطمئن این عکس ممکن نشد (سوژهٔ مشخصی پیدا نشد یا خطر بریدن بخشی از کالا بود)؛ عکس دیگری امتحان کنید.")
            : (made, null);
    }

    /// <summary>ساخت و ذخیرهٔ برش. requireWhite = فقط وقتی لبه‌ها واقعاً سفیدند (برای کار خودکار).</summary>
    private async Task<DesignImage?> MakeCutoutAsync(UploadArea area, string fileName, string stem, bool requireWhite, CancellationToken cancellationToken)
    {
        var path = uploads.GetPath(area, fileName);
        if (path is null)
            return null;
        byte[]? png;
        try
        {
            if (new FileInfo(path).Length > 15 * 1024 * 1024)
                return null;
            using var bitmap = SKBitmap.Decode(path);
            if (bitmap is null || (long)bitmap.Width * bitmap.Height > 40_000_000)
                return null;
            if (requireWhite && !ImageColors.HasWhiteBackground(bitmap))
                return null;
            // دور ۳۰: دوربری مطمئن (مدل + اعتبارسنجی)؛ رد اعتبارسنجی ← null و عکس اصلی همان‌طور استفاده می‌شود.
            png = Fratak_Shop.Web.Infrastructure.Smart.SubjectMatting.Cutout(bitmap, CutoutSide)?.Png;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            logger.LogDebug(exception, "Cutout failed for {File}", fileName);
            return null;
        }
        if (png is null)
            return null;
        var stored = await uploads.SaveImageAsAsync(UploadArea.Cutouts, stem, png, cancellationToken);
        if (stored is null)
            return null;
        return new DesignImage($"/uploads/{UploadArea.Cutouts.Name}/{stored.FileName}", Math.Max(1, stored.Width ?? 1), Math.Max(1, stored.Height ?? 1)) { Cutout = true };
    }

    /// <summary>
    /// عکس فضاسازی دسته برای چیدمان «photo». ترتیب: تصویر فعلی دسته اگر عکس «-photo-» باشد ← اولین عکس «-photo-» پیشنهادها ←
    /// تصویری که مدیر خودش گذاشته (خارج از پیشنهادهای خودکار) اگر افقی، پهنای ≥ ۹۰۰ و نه زمینهٔ سفید باشد. هیچ ← null.
    /// </summary>
    public DesignImage? CategoryPhoto(string? imageName, IReadOnlyList<string>? options)
    {
        var candidates = new List<(string Name, bool Own)>();
        if (CategoryImages.IsPhoto(imageName))
            candidates.Add((imageName!, false));
        foreach (var option in options ?? Array.Empty<string>())
        {
            if (CategoryImages.IsPhoto(option) && candidates.All(c => c.Name != option))
                candidates.Add((option, false));
        }
        if (!string.IsNullOrWhiteSpace(imageName) && CategoryImages.IsSafeName(imageName) && !CategoryImages.IsPhoto(imageName)
            && (options is null || !options.Contains(imageName, StringComparer.Ordinal)))
            candidates.Add((imageName!, true));
        foreach (var (name, own) in candidates)
        {
            var path = uploads.GetPath(UploadArea.Categories, name);
            if (path is null)
                continue;
            try
            {
                using var codec = SKCodec.Create(path);
                if (codec is null || codec.Info.Width < 900 || codec.Info.Width < codec.Info.Height * 1.15)
                    continue;
                if (own)
                {
                    using var bitmap = SKBitmap.Decode(codec);
                    if (bitmap is null || ImageColors.HasWhiteBackground(bitmap))
                        continue;
                }
                return new DesignImage($"/uploads/{UploadArea.Categories.Name}/{name}", codec.Info.Width, codec.Info.Height);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                logger.LogDebug(exception, "Category photo {Name} could not be read", name);
            }
        }
        return null;
    }

    /// <summary>رنگ غالب عکس (برای پالت «رنگ عکس کالا»)؛ عکس نبود/بی‌رنگ ← null.</summary>
    public string? DominantColor(string? url)
    {
        if (Parse(url) is not { } parsed || uploads.GetPath(parsed.Area, parsed.FileName) is not { } path)
            return null;
        try
        {
            if (new FileInfo(path).Length > 15 * 1024 * 1024)
                return null;
            using var bitmap = SKBitmap.Decode(path);
            return bitmap is null ? null : ImageColors.Dominant(bitmap);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            logger.LogDebug(exception, "Dominant colour failed for {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// پاک کردن برش‌هایی که عکس اصلی‌شان دیگر نیست (کالا یا عکسش حذف شده). خروجی: تعداد فایل پاک‌شده.
    /// مثال: «products-a1.webp» در حالی که /uploads/products/a1.* وجود ندارد ← پاک می‌شود.
    /// </summary>
    public int PruneCutouts(int max = 5000)
    {
        var stale = new List<string>();
        foreach (var name in uploads.List(UploadArea.Cutouts, max))
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            var dash = stem.IndexOf('-', StringComparison.Ordinal);
            if (dash <= 0 || !Areas.TryGetValue(stem[..dash], out var area) || area == UploadArea.Cutouts)
            {
                stale.Add(name);
                continue;
            }
            var source = stem[(dash + 1)..];
            var exists = new[] { ".webp", ".png", ".jpg", ".jpeg", ".gif" }.Any(ext => uploads.GetPath(area, source + ext) is not null);
            if (!exists)
                stale.Add(name);
        }
        if (stale.Count > 0)
            uploads.Delete(UploadArea.Cutouts, stale);
        return stale.Count;
    }
}
