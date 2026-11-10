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
    /// <summary>کمترین امتیاز شباهت برای نشان دادن یک کالا.</summary>
    public const double MinScore = 0.62;

    /// <summary>بیشترین تعداد نتیجه.</summary>
    public const int MaxResults = 8;

    /// <summary>تعداد امضای ساخته‌شده در هر نوبت پس‌زمینه.</summary>
    public const int BatchSize = 40;

    private const string CacheKey = "imgsig:all";
    private static readonly SemaphoreSlim Gate = new(2, 2);

    private sealed record Entry(Guid ProductId, long Hash, byte[] Colors);

    /// <summary>جست‌وجوی کالاهای شبیه به عکس (بایت‌های فایل).</summary>
    public async Task<ImageSearchResult> SearchAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken))
            return new ImageSearchResult([], Busy: true);
        try
        {
            var signature = ImageSignature.Compute(data);
            if (signature is null)
                return new ImageSearchResult([], Invalid: true);

            var entries = await LoadAsync(cancellationToken);
            var ids = Rank(signature, entries.Select(e => (e.ProductId, e.Hash, e.Colors)));
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

    private async Task<List<Entry>> LoadAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out List<Entry>? cached) && cached is not null)
            return cached;
        var rows = await context.ProductImageSignatures
            .Where(s => context.Products.Any(p => p.Id == s.ProductId && p.IsActive))
            .Select(s => new { s.ProductId, s.Hash, s.Colors })
            .ToListAsync(cancellationToken);
        var list = rows.Select(r => new Entry(r.ProductId, r.Hash, r.Colors)).ToList();
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

        var pending = await context.Products
            .Where(p => p.IsActive)
            .Select(p => new
            {
                p.Id,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
            })
            .Where(p => p.Image != null
                        && !context.ProductImageSignatures.Any(s => s.ProductId == p.Id && s.ImageName == p.Image))
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        var changed = 0;
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = uploads.GetPath(UploadArea.Products, item.Image!);
            ImageSignature.Signature? signature = null;
            if (path is not null)
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                    signature = ImageSignature.Compute(bytes);
                }
                catch (IOException exception)
                {
                    logger.LogDebug(exception, "Image signature read failed for {Product}", item.Id);
                }
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
}
