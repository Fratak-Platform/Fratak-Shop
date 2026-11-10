using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Seo;

/// <summary>
/// A public URL for sitemap.xml (path relative to the site root).
/// <para>دور سئوی خودکار: <paramref name="Images"/> = آدرس تصویرهای صفحه (برای &lt;image:image&gt; و دیده شدن در گوگل تصاویر).</para>
/// </summary>
public record SitemapEntry(string Path, DateTimeOffset? LastModified, string ChangeFrequency, decimal Priority, IReadOnlyList<string>? Images = null);

public interface ISitemapService
{
    /// <summary>All indexable storefront URLs (cached for a few minutes).</summary>
    Task<IReadOnlyList<SitemapEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the sitemap from the database with light projections (slug + modified date),
/// so it stays cheap even for large catalogues.
/// <para>دور سئوی خودکار: صفحه‌های noindex و صفحه‌هایی که canonical آن‌ها به آدرس دیگری اشاره می‌کند در نقشه نمی‌آیند
/// (سیگنال متناقض به گوگل ممنوع) و تصویرهای هر صفحه (حداکثر ۵) هم فهرست می‌شوند.</para>
/// </summary>
public class SitemapService(FratakShopDbContext context, IMemoryCache cache) : ISitemapService
{
    private const string CacheKey = CacheInvalidation.SitemapKey;
    private const int MaxUrls = 45_000; // sitemap protocol limit is 50,000
    private const int MaxImages = 5;

    public async Task<IReadOnlyList<SitemapEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<SitemapEntry>? cached) && cached is not null)
            return cached;

        var now = DateTimeOffset.UtcNow;
        var entries = new List<SitemapEntry>
        {
            new("/", null, "daily", 1.0m),
            new("/products", null, "daily", 0.8m),
            new("/blog", null, "weekly", 0.6m),
            new("/faq", null, "monthly", 0.5m),
            new("/contact-us", null, "yearly", 0.4m),
        };

        // دور ۲۳: دستهٔ سیستمی و دستهٔ کم‌محتوا (کمتر از ۳ کالای فعال در خود و زیردسته‌های مستقیم) در نقشهٔ سایت نمی‌آید
        // (صفحه‌اش هم noindex است؛ با رسیدن به ۳ کالا خودکار برمی‌گردد).
        var thin = Fratak_Shop.Core.Services.Categories.CategoryService.ThinCategoryProducts;
        var categories = await context.Categories
            .Where(x => x.IsActive && !x.IsSystem && (x.Seo == null || x.Seo.NoIndex != true)
                        && context.Products.Count(p => p.IsActive && (p.CategoryId == x.Id || p.Category.ParentId == x.Id)) >= thin)
            .Select(x => new { x.Slug, x.UpdatedAt, x.ImageName, Canonical = x.Seo != null ? x.Seo.CanonicalUrl : null })
            .ToListAsync(cancellationToken);
        entries.AddRange(categories.Select(x => Entry($"/category/{x.Slug}", x.UpdatedAt, "daily", 0.8m, x.Canonical, Images("categories", x.ImageName))).OfType<SitemapEntry>());

        var brands = await context.Brands
            .Where(x => x.IsActive && (x.Seo == null || x.Seo.NoIndex != true))
            .Select(x => new { x.Slug, x.UpdatedAt, x.LogoName, Canonical = x.Seo != null ? x.Seo.CanonicalUrl : null })
            .ToListAsync(cancellationToken);
        entries.AddRange(brands.Select(x => Entry($"/brand/{x.Slug}", x.UpdatedAt, "weekly", 0.6m, x.Canonical, Images("brands", x.LogoName))).OfType<SitemapEntry>());

        var products = await context.Products
            .Where(x => x.IsActive && (x.PublishedAt == null || x.PublishedAt <= now) && (x.Seo == null || x.Seo.NoIndex != true))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(MaxUrls)
            .Select(x => new
            {
                x.Slug,
                x.UpdatedAt,
                Canonical = x.Seo != null ? x.Seo.CanonicalUrl : null,
                Pictures = x.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).Take(MaxImages).ToList(),
            })
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        entries.AddRange(products.Select(x => Entry($"/product/{x.Slug}", x.UpdatedAt, "weekly", 0.9m, x.Canonical, Images("products", x.Pictures.ToArray()))).OfType<SitemapEntry>());

        var blogCategories = await context.BlogCategories
            .Where(x => x.IsActive && (x.Seo == null || x.Seo.NoIndex != true))
            .Select(x => new { x.Slug, x.UpdatedAt, Canonical = x.Seo != null ? x.Seo.CanonicalUrl : null })
            .ToListAsync(cancellationToken);
        entries.AddRange(blogCategories.Select(x => Entry($"/blog/category/{x.Slug}", x.UpdatedAt, "weekly", 0.5m, x.Canonical, null)).OfType<SitemapEntry>());

        var posts = await context.BlogPosts
            .Where(x => x.IsPublished && (x.PublishedAt == null || x.PublishedAt <= now) && (x.Seo == null || x.Seo.NoIndex != true))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(MaxUrls)
            .Select(x => new { x.Slug, x.UpdatedAt, x.ImageName, Canonical = x.Seo != null ? x.Seo.CanonicalUrl : null })
            .ToListAsync(cancellationToken);
        entries.AddRange(posts.Select(x => Entry($"/blog/{x.Slug}", x.UpdatedAt, "monthly", 0.6m, x.Canonical, Images("blogs", x.ImageName))).OfType<SitemapEntry>());

        var pages = await context.ContentPages
            .Where(x => x.IsActive && (x.Seo == null || x.Seo.NoIndex != true))
            .Select(x => new { x.Slug, x.UpdatedAt, Canonical = x.Seo != null ? x.Seo.CanonicalUrl : null })
            .ToListAsync(cancellationToken);
        entries.AddRange(pages.Select(x => Entry($"/page/{x.Slug}", x.UpdatedAt, "monthly", 0.4m, x.Canonical, null)).OfType<SitemapEntry>());

        // دور ۲۱: صفحه‌های کمپین فعال و در بازهٔ نمایش (کمپین تمام‌شده از نقشه بیرون می‌رود ولی آدرسش ۴۰۴ نمی‌دهد).
        var campaigns = await context.LandingPages
            .Where(x => x.IsActive && (x.StartAt == null || x.StartAt <= now) && (x.EndAt == null || x.EndAt > now))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(200)
            .Select(x => new { x.Slug, x.UpdatedAt })
            .ToListAsync(cancellationToken);
        entries.AddRange(campaigns.Select(x => new SitemapEntry($"/campaign/{x.Slug}", x.UpdatedAt, "daily", 0.6m)));

        IReadOnlyList<SitemapEntry> result = entries.Take(MaxUrls).ToList();
        cache.Set(CacheKey, result, TimeSpan.FromMinutes(10));
        return result;
    }

    /// <summary>
    /// ردیف نقشه، یا null اگر canonical صفحه به آدرس دیگری اشاره کند.
    /// مثال: Entry("/product/a", …, canonical: "https://x.ir/product/b") ← null؛ canonical خالی یا «…/product/a» ← ردیف.
    /// </summary>
    public static SitemapEntry? Entry(string path, DateTimeOffset? modified, string frequency, decimal priority, string? canonical, IReadOnlyList<string>? images) =>
        PointsElsewhere(path, canonical) ? null : new SitemapEntry(path, modified, frequency, priority, images);

    /// <summary>آیا canonical به صفحهٔ دیگری اشاره می‌کند؟ (مقایسهٔ بخش مسیر، بدون دامنه، کوئری و / پایانی)</summary>
    public static bool PointsElsewhere(string path, string? canonical)
    {
        if (string.IsNullOrWhiteSpace(canonical))
            return false;
        var value = canonical.Trim();
        // فقط http(s) آدرس مطلق است؛ روی لینوکس «/product/a» هم Uri مطلق (file://) حساب می‌شود و کوئری‌اش جدا نمی‌شود.
        var target = Uri.TryCreate(value, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https" ? absolute.AbsolutePath : value.Split('?', '#')[0];
        static string Clean(string p) => Uri.UnescapeDataString(p).TrimEnd('/').ToLowerInvariant();
        return Clean(target) != Clean(path);
    }

    /// <summary>
    /// آدرس نسبی تصویرها با همان قاعدهٔ ImageHelper: نام سادهٔ فایل ← «/uploads/{folder}/{name}»؛ آدرس «/…» یا «http…» همان.
    /// </summary>
    public static IReadOnlyList<string>? Images(string folder, params string?[] names)
    {
        var list = names.Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.StartsWith('/') || n.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? n : $"/uploads/{folder}/{Uri.EscapeDataString(n)}")
            .Distinct(StringComparer.Ordinal)
            .Take(MaxImages)
            .ToList();
        return list.Count == 0 ? null : list;
    }
}
