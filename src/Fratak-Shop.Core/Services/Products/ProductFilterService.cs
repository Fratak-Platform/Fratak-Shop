using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Products;

/// <summary>
/// Which product specifications are offered as listing filters (admin managed,
/// global or per category). The value counts come from <see cref="IProductService.GetSpecificationFacetsAsync"/>.
/// </summary>
public interface IProductFilterService
{
    /// <summary>Filters of a listing: global ones plus those of the category and its parents.</summary>
    Task<List<SpecificationFacetRequest>> GetAttributesAsync(string? categorySlug);

    void InvalidateCache();
}

public class ProductFilterService(FratakShopDbContext context, IMemoryCache cache) : IProductFilterService
{
    public async Task<List<SpecificationFacetRequest>> GetAttributesAsync(string? categorySlug)
    {
        var all = await cache.GetOrCreateAsync(CacheKeys.FilterAttributes, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheKeys.DefaultLifetime;
            return await context.ProductFilterAttributes
                .Where(f => f.IsActive)
                .OrderBy(f => f.DisplayOrder)
                .Select(f => new CachedAttribute(f.Slug, f.Title, f.FeatureKey, f.MaxValues, f.CategoryId))
                .ToListAsync();
        }) ?? [];

        if (all.Count == 0)
            return [];

        HashSet<Guid> scope = [];
        if (!string.IsNullOrWhiteSpace(categorySlug) && all.Any(a => a.CategoryId.HasValue))
        {
            var slug = categorySlug.Trim();
            var category = await context.Categories
                .Where(c => c.Slug == slug)
                .Select(c => new { c.Id, c.ParentId })
                .FirstOrDefaultAsync();

            // The category and its ancestors (bounded walk up the tree).
            var currentId = category?.Id;
            var parentId = category?.ParentId;
            for (var depth = 0; currentId.HasValue && depth < 6; depth++)
            {
                scope.Add(currentId.Value);
                currentId = parentId;
                if (currentId.HasValue)
                {
                    var id = currentId.Value;
                    parentId = await context.Categories.Where(c => c.Id == id).Select(c => c.ParentId).FirstOrDefaultAsync();
                }
            }
        }

        return all
            .Where(a => a.CategoryId is null || scope.Contains(a.CategoryId.Value))
            .GroupBy(a => a.FeatureKey)
            .Select(g => g.First())
            .Take(8)
            .Select(a => new SpecificationFacetRequest(a.Slug, a.Title, a.FeatureKey, a.MaxValues))
            .ToList();
    }

    public void InvalidateCache() => cache.Remove(CacheKeys.FilterAttributes);

    private sealed record CachedAttribute(string Slug, string Title, string FeatureKey, int MaxValues, Guid? CategoryId);
}
