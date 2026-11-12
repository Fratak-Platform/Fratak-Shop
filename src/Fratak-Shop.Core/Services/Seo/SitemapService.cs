using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Seo;

/// <summary>
/// A public URL for sitemap.xml (path relative to the site root).
/// <para>دور سئوی خودکار: <paramref name="Images"/> = آدرس تصویرهای صفحه (برای &lt;image:image&gt; و دیده شدن در گوگل تصاویر).</para>
/// </summary>
public record SitemapEntry(string Path, DateTimeOffset? LastModified, string ChangeFrequency, decimal Priority, IReadOnlyList<string>? Images = null,
    IReadOnlyList<SitemapVideo>? Videos = null);

/// <summary>
/// دور ۲۴ (پیشنهاد ۴۷): یک ویدیوی صفحه برای &lt;video:video&gt; نقشهٔ سایت (دیده شدن در «ویدیوهای» گوگل).
/// <para>گوگل این‌ها را لازم دارد: تصویر بندانگشتی، عنوان، توضیح و یکی از «نشانی فایل» یا «نشانی پخش‌کننده».</para>
/// <para>مثال: (‎"/uploads/products/a.webp"، «معرفی هدفون هایلو T15»، «هدفون بی‌سیم با ۲۰ ساعت شارژ…»، ‎"/uploads/videos/a.mp4"، null، 95).</para>
/// </summary>
public sealed record SitemapVideo(string Thumbnail, string Title, string Description, string? ContentUrl, string? PlayerUrl, int? DurationSeconds);

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
public class SitemapService(FratakShopDbContext context, IMemoryCache cache, Autopilot.ISeoHubService? hubs = null) : ISitemapService
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

        // دور ۲۴ (پیشنهاد ۴۰): صفحه‌های «دسته + برند» با حداقل ۳ کالای فعال (همان شرط ایندکس شدن صفحه)؛ دستهٔ noindex/سیستمی نه.
        var combos = await context.Products
            .Where(p => p.IsActive && p.BrandId != null
                        && p.Category.IsActive && !p.Category.IsSystem && (p.Category.Seo == null || p.Category.Seo.NoIndex != true)
                        && p.Brand!.IsActive)
            .GroupBy(p => new { Category = p.Category.Slug, Brand = p.Brand!.Slug })
            .Where(g => g.Count() >= Fratak_Shop.Core.Services.Seo.Autopilot.ListingLandings.MinBrandProducts)
            .Select(g => new { g.Key.Category, g.Key.Brand, Modified = g.Max(p => p.UpdatedAt) })
            .Take(2000)
            .ToListAsync(cancellationToken);
        entries.AddRange(combos.Select(x => new SitemapEntry(
            $"/category/{x.Category}/brand/{x.Brand}", // کدگذاری نشانی هنگام نوشتن XML انجام می‌شود (SeoEndpoints.EncodePath)
            x.Modified, "weekly", 0.6m)));

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
        var videos = await VideosAsync(cancellationToken);
        entries.AddRange(products
            .Select(x => (Row: Entry($"/product/{x.Slug}", x.UpdatedAt, "weekly", 0.9m, x.Canonical, Images("products", x.Pictures.ToArray())), x.Slug))
            .Where(x => x.Row is not null)
            .Select(x => videos.TryGetValue(x.Slug, out var list) ? x.Row! with { Videos = list } : x.Row!));

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

        if (hubs is not null)
            entries.AddRange(await HubEntriesAsync(hubs, thin, cancellationToken));

        IReadOnlyList<SitemapEntry> result = entries.Take(MaxUrls).ToList();
        cache.Set(CacheKey, result, TimeSpan.FromMinutes(10));
        return result;
    }

    /// <summary>
    /// دور ۲۴ (پیشنهادهای ۴۲ تا ۴۴): واژه‌نامه، راهنمای جامع دسته‌ها و صفحه‌های مناسبت – فقط آن‌هایی که قابل ایندکس‌اند
    /// (همان شرط خود صفحه، تا نقشه و صفحه سیگنال متناقض ندهند). راهنما فقط برای ۵۰ دستهٔ پرکالاتر (حداقل ۶ کالا) بررسی می‌شود و
    /// مناسبت‌ها فقط تا ۶۰ روز مانده به تاریخشان (همه از کش سرویس هاب).
    /// </summary>
    private async Task<List<SitemapEntry>> HubEntriesAsync(Autopilot.ISeoHubService hub, int thin, CancellationToken cancellationToken)
    {
        var list = new List<SitemapEntry>();
        if ((await hub.GlossaryAsync(cancellationToken)).Indexable)
            list.Add(new SitemapEntry("/glossary", null, "monthly", 0.4m));

        var minimum = Math.Max(thin, Autopilot.CategoryGuideView.MinProducts);
        var slugs = await context.Categories
            .Where(x => x.IsActive && !x.IsSystem && (x.Seo == null || x.Seo.NoIndex != true))
            .Select(x => new { x.Slug, Count = context.Products.Count(p => p.IsActive && (p.CategoryId == x.Id || p.Category.ParentId == x.Id)) })
            .Where(x => x.Count >= minimum)
            .OrderByDescending(x => x.Count).Take(50)
            .Select(x => x.Slug)
            .ToListAsync(cancellationToken);
        foreach (var slug in slugs)
        {
            if (await hub.GuideAsync(slug, cancellationToken) is { Indexable: true })
                list.Add(new SitemapEntry($"/guide/{slug}", null, "weekly", 0.7m));
        }

        foreach (var occasion in Autopilot.SeoCalendar.Upcoming(IranClock.Today(DateTimeOffset.UtcNow), Array.Empty<string>(), 60))
        {
            if (await hub.OccasionAsync(occasion.Slug, cancellationToken) is { Indexable: true })
                list.Add(new SitemapEntry($"/occasion/{occasion.Slug}", null, "daily", 0.6m));
        }
        return list;
    }

    /// <summary>
    /// دور ۲۴ (پیشنهاد ۴۷): ویدیوهای فعال کالاهای فعال، به تفکیک نامک کالا (حداکثر ۳ ویدیو برای هر کالا، مثل JSON-LD صفحهٔ کالا).
    /// ویدیوی خاموش، منبع نامعتبر یا خاموش بودن «ویدیوی محصول» در تنظیمات ← هیچ ردیفی.
    /// توضیح = عنوان ویدیو + خلاصهٔ کوتاه کالا (متن ساده، حداکثر ۳۰۰ نویسه).
    /// </summary>
    private async Task<Dictionary<string, IReadOnlyList<SitemapVideo>>> VideosAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyList<SitemapVideo>>(StringComparer.Ordinal);
        var enabled = await context.StoreSettings.AsNoTracking().Select(x => (bool?)x.IsProductVideoEnabled).FirstOrDefaultAsync(cancellationToken) ?? true;
        if (!enabled)
            return result;
        var now = DateTimeOffset.UtcNow;
        var rows = await context.ProductVideos.AsNoTracking()
            .Where(v => v.IsActive && v.Product.IsActive && (v.Product.PublishedAt == null || v.Product.PublishedAt <= now))
            .OrderBy(v => v.DisplayOrder).ThenBy(v => v.CreatedAt)
            .Select(v => new
            {
                v.Product.Slug,
                ProductName = v.Product.Name,
                v.Product.ShortDescription,
                v.Title,
                v.Provider,
                v.Source,
                v.PosterImage,
                v.DurationSeconds,
                Picture = v.Product.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
            })
            .Take(5000)
            .ToListAsync(cancellationToken);
        foreach (var group in rows.GroupBy(r => r.Slug))
        {
            var list = new List<SitemapVideo>();
            foreach (var row in group)
            {
                if (list.Count >= 3 || VideoSource.Resolve(row.Provider, row.Source) is not { } resolved)
                    continue;
                var thumbnail = Images("products", row.PosterImage ?? row.Picture)?.FirstOrDefault();
                if (thumbnail is null)
                    continue;
                list.Add(new SitemapVideo(thumbnail, Clip(row.Title ?? row.ProductName, 100), VideoDescription(row.Title, row.ProductName, row.ShortDescription),
                    resolved.FileUrl, resolved.EmbedUrl, row.DurationSeconds is > 0 and <= 28_800 ? row.DurationSeconds : null));
            }
            if (list.Count > 0)
                result[group.Key] = list;
        }
        return result;
    }

    /// <summary>
    /// توضیح ویدیو: «عنوان ویدیو – خلاصهٔ کالا» (متن ساده، حداکثر ۳۰۰ نویسه)؛ بدون خلاصه ← «ویدیوی معرفی {نام کالا}».
    /// مثال: («آنباکس»، «هدفون T15»، «&lt;p&gt;هدفون بی‌سیم…&lt;/p&gt;») ← «آنباکس – هدفون بی‌سیم…».
    /// </summary>
    public static string VideoDescription(string? title, string productName, string? shortDescription)
    {
        var summary = string.IsNullOrWhiteSpace(shortDescription) ? null
            : System.Text.RegularExpressions.Regex.Replace(Fratak_Shop.Core.Services.Seo.Engine.PersianText.HtmlToText(shortDescription), @"\s+", " ").Trim();
        var head = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        var text = (head, summary) switch
        {
            (not null, { Length: > 0 }) => head + " – " + summary,
            (null, { Length: > 0 }) => summary!,
            (not null, _) => head + " – ویدیوی معرفی " + productName.Trim(),
            _ => "ویدیوی معرفی " + productName.Trim(),
        };
        return Clip(text, 300);
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

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
