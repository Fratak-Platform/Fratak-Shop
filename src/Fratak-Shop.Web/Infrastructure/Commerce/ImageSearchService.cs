using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure.Commerce;

/// <summary>نتیجهٔ جست‌وجوی تصویری. Busy = سرور الان شلوغ است؛ Invalid = فایل تصویر معتبری نبود.</summary>
public sealed record ImageSearchResult(IReadOnlyList<ProductFilterData> Products, bool Busy = false, bool Invalid = false);

/// <summary>
/// دور ۲۴ (پیشنهاد ۲۹ – جست‌وجو با عکس): مشتری عکسی می‌فرستد و کالاهای شبیه (شکل و رنگ) پیدا می‌شوند.
/// <para>
/// ساخت امضا (<see cref="BuildSignaturesAsync"/>): هر ۵ دقیقه حداکثر ۴۰ کالای فعال که امضای تصویر اصلی‌شان نیست یا تصویرشان عوض شده
/// امضا می‌گیرند (کار پس‌زمینه در EngagementMaintenanceService). امضای کالای حذف‌شده هم پاک می‌شود.
/// </para>
/// <para>
/// جست‌وجو (<see cref="SearchAsync"/>): امضای عکس مشتری ساخته و با امضای همهٔ کالاها (از حافظه، ۱۰ دقیقه) مقایسه می‌شود؛
/// حداکثر ۸ کالا با امتیاز دست‌کم <see cref="MinScore"/>. عکس مشتری هیچ‌جا ذخیره نمی‌شود.
/// </para>
/// <para>سبکی و ایمنی: حداکثر ۲ جست‌وجوی هم‌زمان (بقیه ۲ ثانیه صبر و بعد «شلوغ»)، حداکثر ۴ مگابایت، سقف پیکسل؛
/// سهمیهٔ جدا در RateLimitingSetup (۱۰ در دقیقه برای هر IP).</para>
/// <para>محل استفاده: POST /api/search/image (CommerceEndpoints) و assets/js/commerce.js (دکمهٔ دوربین کنار جست‌وجو).</para>
/// </summary>
public sealed class ImageSearchService(
    FratakShopDbContext context,
    IProductService products,
    IUploadStore uploads,
    IMemoryCache cache,
    ILogger<ImageSearchService> logger)
{
    /// <summary>
    /// کمترین امتیاز شباهت برای نشان دادن یک کالا. دور ۲۶: با امتیاز تازه (pHash + همهٔ عکس‌ها) همان عکس حدود ۰٫۹۵ و
    /// عکس بی‌ربط حدود ۰٫۳ می‌گیرد؛ ۰٫۵۵ یعنی «واقعاً شبیه».
    /// </summary>
    public const double MinScore = 0.55;

    /// <summary>دور ۲۶: اگر بهترین نتیجه «همان عکس» است (≥ این امتیاز)، نتیجه‌های خیلی ضعیف‌تر از آن (۰٫۲ کمتر) نشان داده نمی‌شوند.</summary>
    public const double ExactScore = 0.88;

    /// <summary>بیشترین تعداد نتیجه.</summary>
    public const int MaxResults = 8;

    /// <summary>تعداد امضای ساخته‌شده در هر نوبت پس‌زمینه (دور ۲۶: ۶۰؛ هر عکس کوچک‌شده باز می‌شود و سبک است).</summary>
    public const int BatchSize = 60;

    private const string CacheKey = "imgsig:all";
    private static readonly SemaphoreSlim Gate = new(2, 2);


    /// <summary>جست‌وجوی کالاهای شبیه به عکس (بایت‌های فایل).</summary>
    public async Task<ImageSearchResult> SearchAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken))
            return new ImageSearchResult([], Busy: true);
        try
        {
            // دور ۲۶: چند نسخه از عکس مشتری (بدون حاشیه، کل قاب، وسط) و مقایسه با همهٔ عکس‌های هر کالا.
            var variants = ImageSignature.ComputeVariants(data);
            if (variants.Count == 0)
                return new ImageSearchResult([], Invalid: true);

            var entries = await LoadAsync(cancellationToken);
            var ids = Rank(variants, entries);
            if (ids.Count == 0)
                return new ImageSearchResult([]);
            var cards = await products.GetCardsByIdsAsync(ids);
            return new ImageSearchResult(cards);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// رتبه‌بندی خالص (برای آزمون): شناسهٔ کالاهایی که امتیازشان دست‌کم <see cref="MinScore"/> است، از بیشترین به کمترین، حداکثر ۸ تا.
    /// </summary>
    public static List<Guid> Rank(ImageSignature.Signature query, IEnumerable<(Guid ProductId, long Hash, byte[] Colors)> candidates) =>
        candidates
            .Select(c => (c.ProductId, Score: ImageSignature.Score(query, c.Hash, c.Colors)))
            .Where(x => x.Score >= MinScore)
            .OrderByDescending(x => x.Score)
            .Take(MaxResults)
            .Select(x => x.ProductId)
            .ToList();

    /// <summary>
    /// دور ۲۶ – رتبه‌بندی خالص با امضای چندعکسی: امتیاز هر کالا = بهترین تطبیق (<see cref="ImageSignature.Score(IReadOnlyList{ImageSignature.Signature}, ImageSignature.Stored)"/>).
    /// اگر بهترین نتیجه «همان عکس» است، نتیجه‌هایی که ۰٫۲ ضعیف‌ترند کنار می‌روند (مشتری دنبال همان کالاست). حداکثر ۸ نتیجه.
    /// مثال: عکس دقیق کالای A ← A اول؛ کالای بی‌ربط هرگز.
    /// </summary>
    public static List<Guid> Rank(IReadOnlyList<ImageSignature.Signature> queries, IEnumerable<ImageSignature.Stored> candidates)
    {
        var scored = candidates
            .Select(c => (c.ProductId, Score: ImageSignature.Score(queries, c)))
            .Where(x => x.Score >= MinScore)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.ProductId)
            .Take(MaxResults)
            .ToList();
        if (scored.Count > 0 && scored[0].Score >= ExactScore)
            scored = scored.Where(x => x.Score >= scored[0].Score - 0.2).ToList();
        return scored.Select(x => x.ProductId).ToList();
    }

    private async Task<List<ImageSignature.Stored>> LoadAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out List<ImageSignature.Stored>? cached) && cached is not null)
            return cached;
        var rows = await context.ProductImageSignatures
            .Where(s => context.Products.Any(p => p.Id == s.ProductId && p.IsActive))
            .Select(s => new { s.ProductId, s.Hash, s.Colors, s.Hashes })
            .ToListAsync(cancellationToken);
        // امضای قدیمی (بدون Hashes) فقط dHash دارد؛ تا ساخت دوباره با همان یک هش مقایسه می‌شود.
        var list = rows.Select(r =>
        {
            var shapes = ImageSignature.Unpack(r.Hashes);
            if (shapes.Count == 0)
                shapes.Add((r.Hash, 0));
            return new ImageSignature.Stored(r.ProductId, shapes, r.Colors);
        }).ToList();
        cache.Set(CacheKey, list, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
        return list;
    }

    /// <summary>
    /// ساخت/به‌روزرسانی امضای حداکثر <see cref="BatchSize"/> کالا و پاک کردن امضای کالاهای حذف‌شده. خروجی: تعداد تغییرها.
    /// </summary>
    public async Task<int> BuildSignaturesAsync(CancellationToken cancellationToken)
    {
        var removed = await context.ProductImageSignatures
            .Where(s => !context.Products.Any(p => p.Id == s.ProductId))
            .ExecuteDeleteAsync(cancellationToken);

        // دور ۲۶: امضا دوباره ساخته می‌شود اگر عکس اصلی عوض شد، تعداد عکس‌ها تغییر کرد یا امضا قدیمی است (بدون Hashes).
        var pending = await context.Products
            .Where(p => p.IsActive)
            .Select(p => new
            {
                p.Id,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
                Count = p.Images.Count,
            })
            .Where(p => p.Image != null
                        && !context.ProductImageSignatures.Any(s => s.ProductId == p.Id && s.ImageName == p.Image && s.ImageCount == p.Count && s.Hashes != null))
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var changed = 0;
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var signature = await SignAsync(item.Image!, item.Id, cancellationToken);
            // دور ۲۶: شکل همهٔ عکس‌های کالا (اصلی اول؛ حداکثر ۶).
            var names = await context.ProductImages.Where(i => i.ProductId == item.Id)
                .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).Take(ImageSignature.MaxShapes).ToListAsync(cancellationToken);
            var shapes = new List<(long, long)>();
            if (signature is not null)
                shapes.Add((signature.Hash, signature.PHash));
            foreach (var name in names.Where(n => !string.Equals(n, item.Image, StringComparison.Ordinal)))
            {
                if (shapes.Count >= ImageSignature.MaxShapes)
                    break;
                if (await SignAsync(name, item.Id, cancellationToken) is { } extra)
                    shapes.Add((extra.Hash, extra.PHash));
            }

            var row = await context.ProductImageSignatures.AsTracking().FirstOrDefaultAsync(s => s.ProductId == item.Id, cancellationToken);
            if (row is null)
            {
                row = new ProductImageSignature { ProductId = item.Id };
                context.ProductImageSignatures.Add(row);
            }
            // تصویر خراب/گمشده هم ثبت می‌شود (با امضای خالی) تا هر ۵ دقیقه دوباره امتحان نشود؛ با عوض شدن تصویر دوباره ساخته می‌شود.
            row.ImageName = item.Image!.Length > 200 ? item.Image[..200] : item.Image;
            row.Hash = signature?.Hash ?? 0;
            row.Colors = signature?.Colors ?? new byte[ImageSignature.Bins];
            row.Hashes = ImageSignature.Pack(shapes);
            row.ImageCount = item.Count;
            row.ComputedAt = DateTimeOffset.UtcNow;
            changed++;
        }
        if (changed > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            cache.Remove(CacheKey);
        }
        return changed + removed;
    }

    /// <summary>امضای یک فایل عکس کالا؛ فایل نبود/خراب بود ← null.</summary>
    private async Task<ImageSignature.Signature?> SignAsync(string imageName, Guid productId, CancellationToken cancellationToken)
    {
        var path = uploads.GetPath(UploadArea.Products, imageName);
        if (path is null || !File.Exists(path))
            return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return ImageSignature.Compute(bytes);
        }
        catch (IOException exception)
        {
            logger.LogDebug(exception, "Image signature read failed for {Product}", productId);
            return null;
        }
    }
}
