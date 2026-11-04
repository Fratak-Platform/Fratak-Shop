using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>یک لینک «فرود» (مثال: «خرید گوشی زیر ۱۰ میلیون تومان» ← /category/mobile?maxPrice=10000000).</summary>
public sealed record SeoLandingLink(string Label, string Url, int Count);

/// <summary>
/// لینک‌های فرود بازهٔ قیمت در صفحهٔ دسته (مورد ۱۹۲ پیشنهاد: «خرید X زیر Y تومان»).
/// <para>بازه‌ها از قیمت واقعی محصولات موجود همان دسته ساخته می‌شوند (<see cref="ContentInsights.PriceBands"/>)؛ بازه‌ای که کمتر
/// از ۴ محصول دارد یا تقریباً همهٔ محصولات را می‌گیرد ساخته نمی‌شود (صفحهٔ کم‌محتوا یا تکراری ممنوع).</para>
/// <para>صفحهٔ فیلترشده همچنان noindex است (رقابت با صفحهٔ اصلی دسته ممنوع)؛ این لینک‌ها برای کاربر (پیدا کردن سریع بودجه) و
/// توزیع اعتبار لینک داخلی‌اند.</para>
/// <para>کجا استفاده می‌شود؟ صفحهٔ دسته (Pages/Search) زیر توضیح دسته.</para>
/// </summary>
public interface ISeoLandingService
{
    /// <summary>لینک‌های بازهٔ قیمت یک دسته (کش ۳۰ دقیقه). مثال: PriceLandingsAsync(id, "mobile", "گوشی موبایل").</summary>
    Task<IReadOnlyList<SeoLandingLink>> PriceLandingsAsync(Guid categoryId, string categorySlug, string categoryName, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISeoLandingService"/>
public sealed class SeoLandingService(FratakShopDbContext context, IMemoryCache cache, ISeoLinkTargetProvider provider) : ISeoLandingService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public async Task<IReadOnlyList<SeoLandingLink>> PriceLandingsAsync(Guid categoryId, string categorySlug, string categoryName, CancellationToken cancellationToken = default)
    {
        // بخش «صفحه‌های فرود» در تنظیمات خلبان خودکار خاموش ← هیچ لینکی.
        var options = await provider.OptionsAsync(cancellationToken);
        if (!options.Can(options.Landings))
            return [];
        var key = "seo:landing:" + categoryId.ToString("N");
        if (cache.TryGetValue(key, out IReadOnlyList<SeoLandingLink>? cached) && cached is not null)
            return cached;

        var now = DateTimeOffset.UtcNow;
        var prices = await context.Products.AsNoTracking()
            .Where(p => p.IsActive && p.CategoryId == categoryId && p.StockQuantity > 0)
            .Select(p => p.DiscountPrice != null && p.DiscountPrice >= 0 && p.DiscountPrice < p.Price
                         && (p.DiscountStartAt == null || p.DiscountStartAt <= now) && (p.DiscountEndAt == null || p.DiscountEndAt >= now)
                ? p.DiscountPrice.Value
                : p.Price)
            .Take(2000)
            .ToListAsync(cancellationToken);

        IReadOnlyList<SeoLandingLink> links = Build(prices, categorySlug, categoryName);
        cache.Set(key, links, Lifetime);
        return links;
    }

    /// <summary>
    /// ساخت لینک‌ها از قیمت‌ها (ایستا برای تست).
    /// <example><code>Build([..۲۰ قیمت..], "mobile", "گوشی") ← [("خرید گوشی زیر ۵ میلیون تومان", "/category/mobile?maxPrice=5000000", 8)]</code></example>
    /// </summary>
    public static List<SeoLandingLink> Build(IReadOnlyList<decimal> prices, string categorySlug, string categoryName) =>
        ContentInsights.PriceBands(prices)
            .Select(b => new SeoLandingLink(
                $"خرید {categoryName.Trim()} {b.Label}",
                $"/category/{Uri.EscapeDataString(categorySlug)}?maxPrice={(long)b.Max}",
                b.Count))
            .ToList();
}
