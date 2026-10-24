using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Seo;

/// <summary>A public URL for sitemap.xml (path relative to the site root).</summary>
public record SitemapEntry(string Path, DateTimeOffset? LastModified, string ChangeFrequency, decimal Priority);

public interface ISitemapService
{
    /// <summary>All indexable storefront URLs (cached for a few minutes).</summary>
    Task<IReadOnlyList<SitemapEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the sitemap from the database with light projections (slug + modified date),
/// so it stays cheap even for large catalogues.
/// </summary>
public class SitemapService(FratakShopDbContext context, IMemoryCache cache) : ISitemapService
{
    private const string CacheKey = "core:sitemap";
    private const int MaxUrls = 45_000; // sitemap protocol limit is 50,000

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

        var categories = await context.Categories
            .Where(x => x.IsActive)
            .Select(x => new { x.Slug, x.UpdatedAt })
            .ToListAsync(cancellationToken);
        entries.AddRange(categories.Select(x => new SitemapEntry($"/category/{x.Slug}", x.UpdatedAt, "daily", 0.8m)));

        var brands = await context.Brands
            .Where(x => x.IsActive)
            .Select(x => new { x.Slug, x.UpdatedAt })
            .ToListAsync(cancellationToken);
        entries.AddRange(brands.Select(x => new SitemapEntry($"/brand/{x.Slug}", x.UpdatedAt, "weekly", 0.6m)));

        var products = await context.Products
            .Where(x => x.IsActive && (x.PublishedAt == null || x.PublishedAt <= now))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(MaxUrls)
            .Select(x => new { x.Slug, x.UpdatedAt })
            .ToListAsync(cancellationToken);
        entries.AddRange(products.Select(x => new SitemapEntry($"/product/{x.Slug}", x.UpdatedAt, "weekly", 0.9m)));

        var blogCategories = await context.BlogCategories
            .Where(x => x.IsActive)
            .Select(x => new { x.Slug, x.UpdatedAt })
            .ToListAsync(cancellationToken);
        entries.AddRange(blogCategories.Select(x => new SitemapEntry($"/blog/category/{x.Slug}", x.UpdatedAt, "weekly", 0.5m)));

        var posts = await context.BlogPosts
            .Where(x => x.IsPublished && (x.PublishedAt == null || x.PublishedAt <= now))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(MaxUrls)
            .Select(x => new { x.Slug, x.UpdatedAt })
            .ToListAsync(cancellationToken);
        entries.AddRange(posts.Select(x => new SitemapEntry($"/blog/{x.Slug}", x.UpdatedAt, "monthly", 0.6m)));

        var pages = await context.ContentPages
            .Where(x => x.IsActive)
            .Select(x => new { x.Slug, x.UpdatedAt })
            .ToListAsync(cancellationToken);
        entries.AddRange(pages.Select(x => new SitemapEntry($"/page/{x.Slug}", x.UpdatedAt, "monthly", 0.4m)));

        IReadOnlyList<SitemapEntry> result = entries.Take(MaxUrls).ToList();
        cache.Set(CacheKey, result, TimeSpan.FromMinutes(10));
        return result;
    }
}
